using Android.Media;
using Android.Views;

namespace GameStream.Client.Video;

/// <summary>
/// Decodifica H.264 (Annex B) com MediaCodec direto para a Surface.
/// O servidor usa baseline 3.1 com SPS/PPS repetidos a cada keyframe (repeat-headers=1),
/// então o decoder não precisa de configuração extra além da resolução.
/// </summary>
public sealed class H264Decoder : IDisposable
{
    private const string Mime = "video/avc";

    private readonly MediaCodec _codec;
    private readonly Thread _drainThread;
    private readonly long _frameUs;
    private volatile bool _running;
    private long _ptsUs;
    private bool _waitKeyFrame = true;

    public H264Decoder(Surface surface, int width, int height, int fps)
    {
        _frameUs = 1_000_000L / Math.Max(1, fps);

        var format = MediaFormat.CreateVideoFormat(Mime, width, height);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            format.SetInteger("low-latency", 1); // MediaFormat.KEY_LOW_LATENCY

        _codec = MediaCodec.CreateDecoderByType(Mime)!;
        _codec.Configure(format, surface, null, MediaCodecConfigFlags.None);
        _codec.Start();

        _running = true;
        _drainThread = new Thread(DrainLoop) { IsBackground = true, Name = "h264-drain" };
        _drainThread.Start();
    }

    /// <summary>Entrega um quadro ao decoder. Copia os bytes, então o buffer pode ser reaproveitado.</summary>
    public void Feed(byte[] data, int offset, int length, bool keyFrame)
    {
        if (!_running) return;

        // Começa (e recomeça, depois de um descarte) sempre em um keyframe.
        if (_waitKeyFrame)
        {
            if (!keyFrame) return;
            _waitKeyFrame = false;
        }

        try
        {
            int index = _codec.DequeueInputBuffer(20_000);
            if (index < 0)
            {
                _waitKeyFrame = true; // decoder atrasado: descarta até o próximo keyframe
                return;
            }

            var input = _codec.GetInputBuffer(index)!;
            if (length > input.Capacity())
            {
                _codec.QueueInputBuffer(index, 0, 0, _ptsUs, MediaCodecBufferFlags.None);
                _waitKeyFrame = true;
                return;
            }

            input.Clear();
            input.Put(data, offset, length);
            _codec.QueueInputBuffer(index, 0, length, _ptsUs, keyFrame ? MediaCodecBufferFlags.KeyFrame : MediaCodecBufferFlags.None);
            _ptsUs += _frameUs;
        }
        catch (Exception)
        {
            // decoder parado ou em estado inválido (sessão encerrando)
        }
    }

    private void DrainLoop()
    {
        var info = new MediaCodec.BufferInfo();
        while (_running)
        {
            try
            {
                int index = _codec.DequeueOutputBuffer(info, 10_000);
                if (index >= 0)
                    _codec.ReleaseOutputBuffer(index, true); // true = desenha na Surface agora
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (!_running) return;
        _running = false;
        _drainThread.Join(300);
        try { _codec.Stop(); } catch (Exception) { }
        try { _codec.Release(); } catch (Exception) { }
    }
}
