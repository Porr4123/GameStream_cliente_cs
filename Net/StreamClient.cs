using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace GameStream.Client.Net;

/// <summary>
/// Protocolo do servidor (ws://host:porta/stream?game=ID&amp;pin=PIN[&amp;sw=largura&amp;sh=altura&amp;fit=fit|fill|stretch]):
///   servidor -> cliente: texto {"t":"hello","game","w","h","fps","audio":{"codec":"aac","rate","ch"}?} | {"t":"error","msg"}
///                     | binário [1][flags][H.264 Annex B] ou [2][0][quadro AAC cru]
///   cliente -> servidor: texto {"t":"key","k":"Z","d":1}
/// </summary>
public sealed class StreamClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly Channel<byte[]> _outbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Largura, altura e fps do vídeo.</summary>
    public event Action<int, int, int>? Hello;

    /// <summary>O servidor vai enviar som: taxa (Hz) e canais. Chega antes de <see cref="Hello"/>. AAC-LC.</summary>
    public event Action<int, int>? AudioConfig;

    /// <summary>Quadro de som (AAC cru). O buffer é reaproveitado: quem recebe precisa copiar antes de retornar.</summary>
    public event Action<byte[], int, int>? Audio;

    /// <summary>Quadro de vídeo. O buffer é reaproveitado: quem recebe precisa copiar antes de retornar.</summary>
    public event Action<byte[], int, int, bool>? Video;

    /// <summary>Mensagem de erro enviada pelo servidor.</summary>
    public event Action<string>? ServerError;

    public StreamClient()
    {
        _ws.Options.CollectHttpResponseDetails = true; // permite ler 401/404/409 do handshake
    }

    /// <param name="screenWidth">Tela do aparelho em pixels (0 = não informar). O servidor adapta a proporção do quadro a ela.</param>
    /// <param name="fit">Como o jogo ocupa o quadro: "fit" (inteiro), "fill" (cortando as bordas) ou "stretch" (esticado).</param>
    public async Task RunAsync(string host, int port, string gameId, string pin, CancellationToken ct,
        int screenWidth = 0, int screenHeight = 0, string fit = "fit")
    {
        var url = $"ws://{host}:{port}/stream?game={Uri.EscapeDataString(gameId)}&pin={Uri.EscapeDataString(pin)}";
        if (screenWidth > 0 && screenHeight > 0)
            url += $"&sw={screenWidth}&sh={screenHeight}&fit={Uri.EscapeDataString(fit)}";
        var uri = new Uri(url);

        try
        {
            await _ws.ConnectAsync(uri, ct);
        }
        catch (WebSocketException)
        {
            throw new InvalidOperationException(_ws.HttpStatusCode switch
            {
                HttpStatusCode.Unauthorized => "PIN incorreto.",
                HttpStatusCode.NotFound => "Jogo não encontrado no servidor.",
                HttpStatusCode.Conflict => "O servidor já está com outro jogador.",
                _ => "Não consegui conectar ao servidor."
            });
        }

        var sendTask = SendLoopAsync(ct);
        try
        {
            await ReceiveLoopAsync(ct);
        }
        finally
        {
            _outbox.Writer.TryComplete();
            try { await sendTask; } catch { /* conexão já encerrada */ }
        }
    }

    public void SendKey(string key, bool down)
    {
        if (_ws.State != WebSocketState.Open) return;
        var json = $"{{\"t\":\"key\",\"k\":\"{JsonEncodedText.Encode(key)}\",\"d\":{(down ? 1 : 0)}}}";
        _outbox.Writer.TryWrite(System.Text.Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Relata ao servidor algo do lado do app; cai no log do servidor como "[cliente] ...".</summary>
    public void SendLog(string message)
    {
        if (_ws.State != WebSocketState.Open) return;
        var json = $"{{\"t\":\"log\",\"msg\":\"{JsonEncodedText.Encode(message)}\"}}";
        _outbox.Writer.TryWrite(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        // ClientWebSocket só aceita um envio por vez: o canal serializa as teclas.
        await foreach (var message in _outbox.Reader.ReadAllAsync(ct))
            await _ws.SendAsync(message, WebSocketMessageType.Text, true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[256 * 1024];

        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            int length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                result = await _ws.ReceiveAsync(buffer.AsMemory(length), ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                length += result.Count;
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
                HandleText(buffer, length);
            else if (length > 2 && buffer[0] == 1)
                Video?.Invoke(buffer, 2, length - 2, (buffer[1] & 1) != 0);
            else if (length > 2 && buffer[0] == 2)
                Audio?.Invoke(buffer, 2, length - 2);
        }
    }

    private void HandleText(byte[] buffer, int length)
    {
        try
        {
            using var doc = JsonDocument.Parse(buffer.AsMemory(0, length));
            var root = doc.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "hello":
                    if (root.TryGetProperty("audio", out var audio) && audio.ValueKind == JsonValueKind.Object &&
                        string.Equals(audio.TryGetProperty("codec", out var codec) ? codec.GetString() : null, "aac", StringComparison.OrdinalIgnoreCase))
                    {
                        AudioConfig?.Invoke(
                            audio.TryGetProperty("rate", out var rate) ? rate.GetInt32() : 48000,
                            audio.TryGetProperty("ch", out var ch) ? ch.GetInt32() : 2);
                    }
                    Hello?.Invoke(root.GetProperty("w").GetInt32(), root.GetProperty("h").GetInt32(), root.GetProperty("fps").GetInt32());
                    break;
                case "error":
                    ServerError?.Invoke(root.GetProperty("msg").GetString() ?? "erro desconhecido");
                    break;
            }
        }
        catch (JsonException)
        {
            // mensagem malformada: ignora
        }
    }

    public void Dispose()
    {
        _outbox.Writer.TryComplete();
        _ws.Abort();
        _ws.Dispose();
    }
}
