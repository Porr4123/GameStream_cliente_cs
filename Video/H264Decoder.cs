using Android.Media;
using Android.Util;
using Android.Views;
using Java.Nio;

namespace GameStream.Client.Video;

/// <summary>
/// Decodifica H.264 (Annex B) com MediaCodec direto para a Surface.
///
/// O decodificador só é aberto quando chega o primeiro keyframe: dele saem o SPS e o PPS, que vão no
/// MediaFormat (csd-0/csd-1). Muitos decodificadores de aparelhos modestos não iniciam, ou iniciam e nunca
/// mostram imagem, quando recebem SPS/PPS só dentro do fluxo. Se um decodificador recusar a configuração,
/// tenta-se o seguinte (por último, os de software) antes de desistir.
/// </summary>
public sealed class H264Decoder : IDisposable
{
    private const string Tag = "GameStream";
    private const string Mime = "video/avc";
    // Um keyframe pode chegar perto do tamanho do VBV do servidor (bufsize = teto de taxa / 2: 8000 kbps -> ~500 KB).
    private const int MaxInput = 1024 * 1024;
    private const int MaxErrors = 20;

    // Nomes dos decodificadores de software do Android, usados se o padrão (hardware) falhar.
    private static readonly string[] SoftwareDecoders = { "c2.android.avc.decoder", "OMX.google.h264.decoder" };

    private readonly Surface _surface;
    private readonly int _width;
    private readonly int _height;
    private readonly long _frameUs;

    private MediaCodec? _codec;
    private Thread? _drainThread;
    private volatile bool _running = true;
    private volatile bool _waitKeyFrame = true;
    private volatile bool _failed;
    private long _ptsUs;
    private int _feedErrors;
    private string _codecName = "-";

    // Diagnóstico
    private long _received, _keyReceived, _dropped, _rendered;

    /// <summary>A primeira imagem foi desenhada na tela.</summary>
    public event Action? FirstFrame;

    /// <summary>Falha que impede de mostrar o vídeo (mensagem para o usuário).</summary>
    public event Action<string>? Error;

    public bool HasRendered => Interlocked.Read(ref _rendered) > 0;

    /// <summary>O decodificador desistiu (a mensagem foi enviada por <see cref="Error"/>).</summary>
    public bool Failed => _failed;

    public H264Decoder(Surface surface, int width, int height, int fps)
    {
        _surface = surface;
        _width = width;
        _height = height;
        _frameUs = 1_000_000L / Math.Max(1, fps);
    }

    /// <summary>Uma linha com os contadores, para mostrar na tela quando a imagem não aparece.</summary>
    public string StatusLine =>
        $"Vídeo: {Interlocked.Read(ref _received)} quadros recebidos ({Interlocked.Read(ref _keyReceived)} keyframes), " +
        $"{Interlocked.Read(ref _rendered)} exibidos, {Interlocked.Read(ref _dropped)} descartados. Decodificador: {_codecName}";

    /// <summary>Entrega um quadro ao decoder. Copia os bytes, então o buffer pode ser reaproveitado.</summary>
    public void Feed(byte[] data, int offset, int length, bool keyFrame)
    {
        if (!_running || _failed) return;

        Interlocked.Increment(ref _received);
        if (keyFrame) Interlocked.Increment(ref _keyReceived);

        // Começa (e recomeça, depois de um descarte) sempre em um keyframe.
        if (_waitKeyFrame && !keyFrame)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        if (_codec == null)
        {
            if (!Open(data, offset, length)) return;
        }

        try
        {
            var codec = _codec!;
            int index = codec.DequeueInputBuffer(20_000);
            if (index < 0)
            {
                _waitKeyFrame = true; // decoder atrasado: descarta até o próximo keyframe
                Interlocked.Increment(ref _dropped);
                return;
            }

            var input = codec.GetInputBuffer(index)!;
            if (length > input.Capacity())
            {
                Log.Warn(Tag, $"quadro de {length} bytes maior que o buffer de entrada ({input.Capacity()})");
                codec.QueueInputBuffer(index, 0, 0, _ptsUs, MediaCodecBufferFlags.None);
                _waitKeyFrame = true;
                Interlocked.Increment(ref _dropped);
                return;
            }

            input.Clear();
            input.Put(data, offset, length);
            codec.QueueInputBuffer(index, 0, length, _ptsUs, keyFrame ? MediaCodecBufferFlags.KeyFrame : MediaCodecBufferFlags.None);
            _ptsUs += _frameUs;
            _waitKeyFrame = false;
            _feedErrors = 0;
        }
        catch (Exception ex)
        {
            if (!_running) return; // sessão encerrando
            Log.Warn(Tag, $"erro ao entregar quadro ao decodificador: {ex}");
            _waitKeyFrame = true;
            if (++_feedErrors >= MaxErrors) Fail($"Falha no decodificador de vídeo: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- abertura do MediaCodec

    /// <summary>Abre o decodificador usando o SPS/PPS do keyframe. Retorna false se nenhum aceitou (e avisa).</summary>
    private bool Open(byte[] data, int offset, int length)
    {
        var p = AnnexB.ExtractParams(data, offset, length);
        bool hasCsd = p.Sps != null && p.Pps != null;
        Log.Info(Tag, $"primeiro keyframe: {length} bytes, SPS={p.Sps?.Length}, PPS={p.Pps?.Length}");

        // (nome do decodificador ou null = padrão do aparelho, baixa latência)
        var attempts = new List<(string? Name, bool LowLatency)>();
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) attempts.Add((null, true));
        attempts.Add((null, false));
        foreach (var name in SoftwareDecoders) attempts.Add((name, false));

        Exception? last = null;
        foreach (var (name, lowLatency) in attempts)
        {
            if (!_running) return false;

            MediaCodec? codec = null;
            try
            {
                codec = name != null ? MediaCodec.CreateByCodecName(name)! : MediaCodec.CreateDecoderByType(Mime)!;
                codec.Configure(BuildFormat(p, lowLatency), _surface, null, MediaCodecConfigFlags.None);
                codec.Start();

                _codecName = codec.Name ?? name ?? "padrão";
                _codec = codec;
                Log.Info(Tag, $"decodificador aberto: {_codecName} (baixa latência={lowLatency}, csd={hasCsd})");

                _drainThread = new Thread(DrainLoop) { IsBackground = true, Name = "h264-drain" };
                _drainThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"decodificador {name ?? "padrão"} (baixa latência={lowLatency}) falhou: {ex}");
                last = ex;
                try { codec?.Release(); } catch (Exception) { /* ignora */ }
            }
        }

        Fail($"Nenhum decodificador H.264 do aparelho aceitou o vídeo ({last?.Message ?? "sem detalhes"}).");
        return false;
    }

    private MediaFormat BuildFormat(AnnexB.Params p, bool lowLatency)
    {
        var format = MediaFormat.CreateVideoFormat(Mime, _width, _height);
        format.SetInteger("max-input-size", MaxInput); // MediaFormat.KEY_MAX_INPUT_SIZE

        if (p.Sps != null && p.Pps != null)
        {
            format.SetByteBuffer("csd-0", ByteBuffer.Wrap(p.Sps));
            format.SetByteBuffer("csd-1", ByteBuffer.Wrap(p.Pps));
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            format.SetInteger("priority", 0); // MediaFormat.KEY_PRIORITY: tempo real
        if (lowLatency && OperatingSystem.IsAndroidVersionAtLeast(30))
            format.SetInteger("low-latency", 1); // MediaFormat.KEY_LOW_LATENCY
        return format;
    }

    private void Fail(string message)
    {
        if (_failed) return;
        _failed = true;
        Log.Error(Tag, message);
        Error?.Invoke(message);
    }

    // ---------------------------------------------------------------- saída

    private void DrainLoop()
    {
        var codec = _codec!;
        var info = new MediaCodec.BufferInfo();
        int errors = 0;

        while (_running)
        {
            try
            {
                int index = codec.DequeueOutputBuffer(info, 10_000);
                if (index >= 0)
                {
                    codec.ReleaseOutputBuffer(index, true); // true = desenha na Surface agora
                    errors = 0;
                    if (Interlocked.Increment(ref _rendered) == 1)
                    {
                        Log.Info(Tag, "primeira imagem exibida");
                        FirstFrame?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_running) break; // decoder parado de propósito
                // Erro passado do MediaCodec (ex.: CodecException): não encerra a saída, senão o vídeo congela.
                Log.Warn(Tag, $"erro na saída do decodificador: {ex}");
                if (++errors >= MaxErrors)
                {
                    Fail($"Falha no decodificador de vídeo: {ex.Message}");
                    break;
                }
                Thread.Sleep(5);
            }
        }
    }

    public void Dispose()
    {
        if (!_running) return;
        _running = false;
        _drainThread?.Join(300);
        try { _codec?.Stop(); } catch (Exception) { }
        try { _codec?.Release(); } catch (Exception) { }
    }
}
