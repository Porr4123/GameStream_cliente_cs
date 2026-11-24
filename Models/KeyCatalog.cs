namespace GameStream.Client.Models;

/// <summary>Teclas que o usuário pode escolher para um botão: números, letras, setas, Ctrl, Shift e Esc.</summary>
public static class KeyCatalog
{
    public static readonly string[] Numbers = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };

    public static readonly string[] Letters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()).ToArray();

    public static readonly string[] Arrows = { "UP", "DOWN", "LEFT", "RIGHT" };

    public static readonly string[] Special = { "CTRL", "SHIFT", "ESC" };

    /// <summary>Todas as teclas que um layout envia (botões e as quatro direções dos joysticks), em maiúsculas.</summary>
    public static IEnumerable<string> KeysOf(IEnumerable<ButtonLayout> layout)
    {
        foreach (var c in layout)
        {
            if (c.Type == "dpad")
            {
                foreach (var k in c.Keys ?? new List<string>())
                    if (!string.IsNullOrEmpty(k)) yield return k.ToUpperInvariant();
            }
            else if (!string.IsNullOrEmpty(c.Key))
            {
                yield return c.Key.ToUpperInvariant();
            }
        }
    }

    /// <summary>
    /// true se o layout tem Ctrl e Esc: o servidor recusa Esc com Ctrl apertado (e o contrário), porque Ctrl+Esc abriria
    /// o menu Iniciar do Windows. Os dois botões funcionam sozinhos, mas nunca juntos.
    /// </summary>
    public static bool HasBlockedCombo(IEnumerable<ButtonLayout> layout)
    {
        var keys = new HashSet<string>(KeysOf(layout));
        return keys.Contains("CTRL") && keys.Contains("ESC");
    }

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
