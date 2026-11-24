using Android.Media;
using Android.Util;
using Java.Nio;

namespace GameStream.Client.Video;

/// <summary>
/// Toca o som do jogo: recebe quadros AAC-LC "crus" (48 kHz estéreo, sem cabeçalho ADTS), decodifica com MediaCodec
/// e escreve o PCM num AudioTrack de baixa latência. Se algo falhar, o vídeo continua normalmente, sem som.
/// </summary>
public sealed class AacPlayer : IDisposable
{
    private const string Tag = "GameStream";
    private const string Mime = "audio/mp4a-latm";
    private const int MaxFeedErrors = 20;

    private readonly int _rate;
    private readonly int _channels;
    private MediaCodec? _codec;
    private AudioTrack? _track;
    private Thread? _drainThread;
    private volatile bool _running = true;
    private volatile bool _failed;
    private long _ptsUs;
    private int _feedErrors;
    private long _fed, _dropped, _played;

    public AacPlayer(int sampleRate, int channels)
    {
        _rate = sampleRate > 0 ? sampleRate : 48000;
        _channels = channels is 1 or 2 ? channels : 2;
    }

    public bool Failed => _failed;

    public string StatusLine => $"Som: {Interlocked.Read(ref _fed)} quadros recebidos, {Interlocked.Read(ref _played)} tocados, {Interlocked.Read(ref _dropped)} descartados";

    /// <summary>Abre o decodificador e a saída de áudio. Devolve false se o aparelho não conseguir (o app segue sem som).</summary>
    public bool Start()
    {
        try
        {
            // AudioSpecificConfig de 2 bytes: objectType(5 bits)=2 (AAC-LC) | freqIndex(4)=3 (48000) / 4 (44100) | canais(4) | 000
            int freqIndex = _rate switch { 48000 => 3, 44100 => 4, 32000 => 5, 24000 => 6, 22050 => 7, _ => 3 };
            int asc = (2 << 11) | (freqIndex << 7) | (_channels << 3);

            var format = MediaFormat.CreateAudioFormat(Mime, _rate, _channels)!;
            format.SetByteBuffer("csd-0", ByteBuffer.Wrap(new byte[] { (byte)((asc >> 8) & 0xFF), (byte)(asc & 0xFF) }));
            format.SetInteger(MediaFormat.KeyIsAdts, 0);
            format.SetInteger(MediaFormat.KeyMaxInputSize, 16 * 1024);

            _codec = MediaCodec.CreateDecoderByType(Mime);
            _codec.Configure(format, null, null, MediaCodecConfigFlags.None);
            _codec.Start();

            var channelMask = _channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
            int minBuffer = AudioTrack.GetMinBufferSize(_rate, channelMask, Encoding.Pcm16bit);
            if (minBuffer <= 0) minBuffer = _rate * _channels * 2 / 10;

            var builder = new AudioTrack.Builder()
                .SetAudioAttributes(new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Game)!
                    .SetContentType(AudioContentType.Music)!
                    .Build()!)
                .SetAudioFormat(new AudioFormat.Builder()
                    .SetEncoding(Encoding.Pcm16bit)!
                    .SetSampleRate(_rate)!
                    .SetChannelMask(channelMask)!
                    .Build()!)
                .SetBufferSizeInBytes(minBuffer * 3)
                .SetTransferMode(AudioTrackMode.Stream);
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                builder.SetPerformanceMode(AudioTrackPerformanceMode.LowLatency);

            _track = builder.Build();
            _track.Play();

            _drainThread = new Thread(DrainLoop) { IsBackground = true, Name = "GameStream audio" };
            _drainThread.Start();
            Log.Info(Tag, $"som: AAC {_rate} Hz, {_channels} canal(is), buffer {minBuffer * 3} bytes");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"som desligado: {ex}");
            Fail();
            return false;
        }
    }

    /// <summary>Um quadro AAC cru. Copia os bytes, então o buffer pode ser reaproveitado.</summary>
    public void Feed(byte[] data, int offset, int length)
    {
        if (!_running || _failed || _codec == null || length <= 0) return;
        Interlocked.Increment(ref _fed);
        try
        {
            int index = _codec.DequeueInputBuffer(5_000);
            if (index < 0)
            {
                Interlocked.Increment(ref _dropped); // decodificador/saída atrasados: som atrasado é pior que um estalo
                return;
            }

            var input = _codec.GetInputBuffer(index)!;
            if (length > input.Capacity())
            {
                _codec.QueueInputBuffer(index, 0, 0, _ptsUs, MediaCodecBufferFlags.None);
                Interlocked.Increment(ref _dropped);
                return;
            }

            input.Clear();
            input.Put(data, offset, length);
            _codec.QueueInputBuffer(index, 0, length, _ptsUs, MediaCodecBufferFlags.None);
            _ptsUs += 1_000_000L * 1024 / _rate; // 1024 amostras por quadro AAC
            _feedErrors = 0;
        }
        catch (Exception ex)
        {
            if (!_running) return;
            Log.Warn(Tag, $"erro ao entregar som ao decodificador: {ex.Message}");
            if (++_feedErrors >= MaxFeedErrors) Fail();
        }
    }

    private void DrainLoop()
    {
        var info = new MediaCodec.BufferInfo();
        byte[] pcm = new byte[16 * 1024];
        while (_running && !_failed)
        {
            try
            {
                var codec = _codec;
                var track = _track;
                if (codec == null || track == null) return;

                int index = codec.DequeueOutputBuffer(info, 10_000);
                if (index >= 0)
                {
                    if (info.Size > 0)
                    {
                        var output = codec.GetOutputBuffer(index)!;
                        output.Position(info.Offset);
                        output.Limit(info.Offset + info.Size);
                        if (pcm.Length < info.Size) pcm = new byte[info.Size];
                        output.Get(pcm, 0, info.Size);
                        track.Write(pcm, 0, info.Size); // bloqueia até haver espaço: é o relógio do som
                        Interlocked.Increment(ref _played);
                    }
                    codec.ReleaseOutputBuffer(index, false);
                }
            }
            catch (Exception ex)
            {
                if (_running) Log.Warn(Tag, $"som parou: {ex.Message}");
                return;
            }
        }
    }

    private void Fail()
    {
        _failed = true;
        Release();
    }

    private void Release()
    {
        try { _track?.Stop(); } catch { /* ignora */ }
        try { _track?.Release(); } catch { /* ignora */ }
        try { _codec?.Stop(); } catch { /* ignora */ }
        try { _codec?.Release(); } catch { /* ignora */ }
        _track = null;
        _codec = null;
    }

    public void Dispose()
    {
        _running = false;
        try { _drainThread?.Join(500); } catch { /* ignora */ }
        Release();
    }
}
