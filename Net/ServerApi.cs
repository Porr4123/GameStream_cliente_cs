using System.Net;
using System.Text.Json;
using GameStream.Client.Models;

namespace GameStream.Client.Net;

public static class ServerApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>GET /games?pin=... : lista os jogos, teclas permitidas e layout padrão dos botões.</summary>
    public static async Task<List<GameInfo>> GetGamesAsync(string host, int port, string pin, CancellationToken ct = default)
    {
        var url = $"http://{host}:{port}/games?pin={Uri.EscapeDataString(pin)}";
        using var response = await Http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("PIN incorreto.");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, ClientJson.Default.ListGameInfo, ct) ?? new List<GameInfo>();
    }
}
