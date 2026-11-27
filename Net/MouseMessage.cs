using System.Globalization;

namespace GameStream.Client.Net;

/// <summary>Monta a mensagem <c>{"t":"mouse","a":...,"b":...,"x":...,"y":...}</c> (sem reflexão, independente da cultura do aparelho).</summary>
public static class MouseMessage
{
    /// <summary>
    /// action: move | down | up | click; button: left | right; x, y: posição no quadro de vídeo, de 0 a 1.
    /// Devolve null se algo for inválido (ação/botão desconhecidos, NaN, infinito): nada deve ser enviado.
    /// </summary>
    public static string? Build(string action, string button, double x, double y)
    {
        if (action is not ("move" or "down" or "up" or "click")) return null;
        if (button is not ("left" or "right")) return null;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;

        x = Math.Clamp(x, 0.0, 1.0);
        y = Math.Clamp(y, 0.0, 1.0);
        // "0.####" na cultura invariante: ponto decimal mesmo em aparelho em português (vírgula quebraria o JSON)
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"t\":\"mouse\",\"a\":\"{action}\",\"b\":\"{button}\",\"x\":{x:0.####},\"y\":{y:0.####}}}");
    }
}
