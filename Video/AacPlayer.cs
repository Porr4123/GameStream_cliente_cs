using Android.Media;
using Android.Util;
using Java.Nio;

namespace GameStream.Client.Video;

/// <summary>
/// Toca o som do jogo: recebe quadros AAC-LC "crus" (48 kHz estéreo, sem cabeçalho ADTS), decodifica com MediaCodec
/// e escreve o PCM num AudioTrack de baixa latência. Se algo falhar, o vídeo continua normalmente, sem som.
///
/// O formato que sai do decodificador pode diferir do anunciado (outra taxa, mono, PCM float): ao receber
/// INFO_OUTPUT_FORMAT_CHANGED o AudioTrack é refeito para combinar com ele.
/// </summary>
public sealed class AacPlayer : IDisposable
{
    private const string Tag = "GameStream";
    private const string Mime = "audio/mp4a-latm";
    private const int MaxFeedErrors = 20;
    private const int InfoOutputFormatChanged = -2; // MediaCodec.INFO_OUTPUT_FORMAT_CHANGED
    private const int Pcm16 = 2;                    // AudioFormat.ENCODING_PCM_16BIT (padrão do decodificador)
    private const int PcmFloat = 4;                 // AudioFormat.ENCODING_PCM_FLOAT

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

    // O que o decodificador realmente entrega (começa igual ao anunciado pelo servidor).
    private int _outRate, _outChannels;
    private bool _outFloat;
    private string _outputFormat;
    private string _codecName = "-";
    private int _peak;            // maior amplitude do PCM desde o último relatório
    private int _writeErrors;

    public AacPlayer(int sampleRate, int channels)
    {
        _rate = sampleRate > 0 ? sampleRate : 48000;
        _channels = channels is 1 or 2 ? channels : 2;
        _outRate = _rate;
        _outChannels = _channels;
        _outputFormat = $"{_rate} Hz, {_channels} canais (anunciado)";
    }

    public bool Failed => _failed;

    /// <summary>Motivo da falha ao abrir o som (vazio se abriu).</summary>
    public string LastError { get; private set; } = "";

    /// <summary>
    /// Estado para diagnóstico. "pico" é a maior amplitude do som decodificado desde o relatório anterior:
    /// 0 = o decodificador entrega silêncio; alto = o som chega ao AudioTrack (aí o problema é volume/saída do aparelho).
    /// </summary>
    public string Report
    {
        get
        {
            string play = "-";
            var track = _track;
            if (track != null)
            {
                try { play = track.PlayState.ToString(); } catch { play = "?"; }
            }
            int peak = Interlocked.Exchange(ref _peak, 0);
            return $"{StatusLine}; decodificador={_codecName}; saída={_outputFormat}; pico={peak}/32768; trilha={play}; erros de escrita={_writeErrors}; falhou={_failed}";
        }
    }

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
            _codecName = _codec.Name ?? "?";

            _track = CreateTrack(_rate, _channels);

            _drainThread = new Thread(DrainLoop) { IsBackground = true, Name = "GameStream audio" };
            _drainThread.Start();
            Log.Info(Tag, $"som: AAC {_rate} Hz, {_channels} canal(is), decodificador {_codecName}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"som desligado: {ex}");
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            Fail();
            return false;
        }
    }

    /// <summary>AudioTrack em modo stream, PCM 16 bits, já tocando.</summary>
    private static AudioTrack CreateTrack(int rate, int channels)
    {
        var channelMask = channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
        int minBuffer = AudioTrack.GetMinBufferSize(rate, channelMask, Encoding.Pcm16bit);
        if (minBuffer <= 0) minBuffer = rate * channels * 2 / 10;

        var builder = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Game)!
                .SetContentType(AudioContentType.Music)!
                .Build()!)
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(Encoding.Pcm16bit)!
                .SetSampleRate(rate)!
                .SetChannelMask(channelMask)!
                .Build()!)
            .SetBufferSizeInBytes(minBuffer * 3)
            .SetTransferMode(AudioTrackMode.Stream);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            builder.SetPerformanceMode(AudioTrackPerformanceMode.LowLatency);

        var track = builder.Build()!;
        track.Play();
        return track;
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
        short[] converted = new short[8 * 1024];
        int errors = 0;

        while (_running && !_failed)
        {
            try
            {
                var codec = _codec;
                if (codec == null || _track == null) return;

                int index = codec.DequeueOutputBuffer(info, 10_000);
                if (index == InfoOutputFormatChanged)
                {
                    OnOutputFormatChanged(codec);
                    continue;
                }
                if (index < 0) continue; // nada pronto ainda

                try
                {
                    if (info.Size > 0)
                    {
                        var output = codec.GetOutputBuffer(index)!;
                        output.Position(info.Offset);
                        output.Limit(info.Offset + info.Size);
                        if (pcm.Length < info.Size) pcm = new byte[info.Size];
                        output.Get(pcm, 0, info.Size);

                        if (_outFloat)
                        {
                            if (converted.Length < info.Size / 4) converted = new short[info.Size / 4];
                            int count = Pcm.FloatToPcm16(pcm, info.Size, converted);
                            NotePeak(Pcm.Peak16(converted, count));
                            WriteShorts(converted, count);
                        }
                        else
                        {
                            NotePeak(Pcm.Peak16(pcm, info.Size));
                            WriteBytes(pcm, info.Size);
                        }
                        Interlocked.Increment(ref _played);
                    }
                }
                finally
                {
                    codec.ReleaseOutputBuffer(index, false);
                }
                errors = 0;
            }
            catch (Exception ex)
            {
                if (!_running) return; // encerrando de propósito
                // Erro passageiro do MediaCodec/AudioTrack: tenta de novo em vez de calar o som para sempre.
                Log.Warn(Tag, $"erro na saída do som: {ex.Message}");
                if (++errors >= MaxFeedErrors)
                {
                    LastError = $"erros demais na saída do som: {ex.Message}";
                    Log.Warn(Tag, "som desligado: erros demais na saída");
                    return;
                }
                Thread.Sleep(5);
            }
        }
    }

    private void NotePeak(int peak)
    {
        if (peak > Volatile.Read(ref _peak)) Volatile.Write(ref _peak, peak);
    }

    /// <summary>
    /// O decodificador informou o formato real do PCM. Se for diferente do AudioTrack aberto (outra taxa ou número de
    /// canais), refaz o AudioTrack; se for PCM float, passa a converter para 16 bits.
    /// </summary>
    private void OnOutputFormatChanged(MediaCodec codec)
    {
        var f = codec.OutputFormat;
        if (f == null) return;

        int rate = f.ContainsKey(MediaFormat.KeySampleRate) ? f.GetInteger(MediaFormat.KeySampleRate) : _rate;
        int channels = f.ContainsKey(MediaFormat.KeyChannelCount) ? f.GetInteger(MediaFormat.KeyChannelCount) : _channels;
        int encoding = f.ContainsKey(MediaFormat.KeyPcmEncoding) ? f.GetInteger(MediaFormat.KeyPcmEncoding) : Pcm16;

        _outputFormat = $"{rate} Hz, {channels} canais, {(encoding == Pcm16 ? "16 bits" : encoding == PcmFloat ? "float" : "codificação " + encoding)}";
        Log.Info(Tag, "som: formato de saída do decodificador: " + _outputFormat);

        if ((encoding != Pcm16 && encoding != PcmFloat) || channels is < 1 or > 2 || rate <= 0)
        {
            LastError = "saída do decodificador não suportada: " + _outputFormat;
            Log.Warn(Tag, "som desligado: " + LastError);
            Fail();
            return;
        }

        _outFloat = encoding == PcmFloat;
        if (rate != _outRate || channels != _outChannels)
        {
            _outRate = rate;
            _outChannels = channels;
            RebuildTrack();
        }
    }

    /// <summary>Refaz o AudioTrack com a taxa/canais atuais da saída do decodificador.</summary>
    private void RebuildTrack()
    {
        var old = _track;
        _track = CreateTrack(_outRate, _outChannels);
        try { old?.Stop(); } catch { /* ignora */ }
        try { old?.Release(); } catch { /* ignora */ }
    }

    /// <summary>Resultado negativo do AudioTrack.Write: conta, e refaz o AudioTrack algumas vezes antes de desistir.</summary>
    private void HandleWriteError(int code)
    {
        _writeErrors++;
        Log.Warn(Tag, $"som: AudioTrack.Write devolveu o erro {code}");
        if (_writeErrors > MaxFeedErrors) throw new InvalidOperationException($"AudioTrack.Write falhou repetidamente (erro {code})");
        RebuildTrack();
    }

    private void WriteBytes(byte[] pcm, int size)
    {
        int offset = 0, empty = 0;
        while (offset < size && _running)
        {
            var track = _track ?? throw new InvalidOperationException("AudioTrack fechado");
            int written = track.Write(pcm, offset, size - offset); // bloqueia até haver espaço: é o relógio do som
            if (written < 0) { HandleWriteError(written); continue; }
            if (written == 0)
            {
                if (++empty >= 50) throw new InvalidOperationException("AudioTrack não aceitou dados");
                Thread.Sleep(2);
                continue;
            }
            offset += written;
        }
    }

    private void WriteShorts(short[] samples, int count)
    {
        int offset = 0, empty = 0;
        while (offset < count && _running)
        {
            var track = _track ?? throw new InvalidOperationException("AudioTrack fechado");
            int written = track.Write(samples, offset, count - offset);
            if (written < 0) { HandleWriteError(written); continue; }
            if (written == 0)
            {
                if (++empty >= 50) throw new InvalidOperationException("AudioTrack não aceitou dados");
                Thread.Sleep(2);
                continue;
            }
            offset += written;
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
