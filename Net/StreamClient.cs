using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace GameStream.Client.Net;

/// <summary>
/// Protocolo do servidor (ws://host:porta/stream?game=ID&amp;pin=PIN):
///   servidor -> cliente: texto {"t":"hello","game","w","h","fps"} | {"t":"error","msg"} | binário [1][flags][H.264 Annex B]
///   cliente -> servidor: texto {"t":"key","k":"Z","d":1}
/// </summary>
public sealed class StreamClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly Channel<byte[]> _outbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Largura, altura e fps do vídeo.</summary>
    public event Action<int, int, int>? Hello;

    /// <summary>Quadro de vídeo. O buffer é reaproveitado: quem recebe precisa copiar antes de retornar.</summary>
    public event Action<byte[], int, int, bool>? Video;

    /// <summary>Mensagem de erro enviada pelo servidor.</summary>
    public event Action<string>? ServerError;

    public StreamClient()
    {
        _ws.Options.CollectHttpResponseDetails = true; // permite ler 401/404/409 do handshake
    }

    public async Task RunAsync(string host, int port, string gameId, string pin, CancellationToken ct)
    {
        var uri = new Uri($"ws://{host}:{port}/stream?game={Uri.EscapeDataString(gameId)}&pin={Uri.EscapeDataString(pin)}");

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
