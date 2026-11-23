namespace GameStream.Client.Models;

/// <summary>Teclas que o usuário pode escolher para um botão: números, letras, Ctrl, Shift e Esc.</summary>
public static class KeyCatalog
{
    public static readonly string[] Numbers = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };

    public static readonly string[] Letters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()).ToArray();

    public static readonly string[] Special = { "CTRL", "SHIFT", "ESC" };

    /// <summary>Texto curto para mostrar no botão (o nome enviado ao servidor continua em maiúsculas).</summary>
    public static string Display(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        return key.ToUpperInvariant() switch
        {
            "CTRL" => "Ctrl",
            "SHIFT" => "Shift",
            "ESC" => "Esc",
            "ENTER" => "Enter",
            "SPACE" => "Espaço",
            "UP" => "↑",
            "DOWN" => "↓",
            "LEFT" => "←",
            "RIGHT" => "→",
            var k when k.Length > 1 => char.ToUpperInvariant(k[0]) + k[1..].ToLowerInvariant(),
            var k => k
        };
    }
}
