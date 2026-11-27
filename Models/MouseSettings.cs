using System.Text.Json;

namespace GameStream.Client.Models;

/// <summary>Caixa em fração da área do vídeo (origem no canto superior esquerdo).</summary>
public readonly record struct MouseBox(double X, double Y, double W, double H);

/// <summary>
/// Configuração do mouse que o servidor manda no <c>hello</c> (<c>"mouse": {...}</c>).
/// Servidor antigo (sem o campo) ou <c>enabled</c> ausente = desligado: o app fica como antes.
/// </summary>
public sealed record MouseSettings(bool Enabled, IReadOnlyList<MouseBox> Exclude, double ButtonMargin, int HoldMs, int DoubleTapMs)
{
    public const double DefaultButtonMargin = 0.02;
    public const int DefaultHoldMs = 350;
    public const int DefaultDoubleTapMs = 300;
    private const int MaxBoxes = 32;

    public static MouseSettings Disabled { get; } =
        new(false, Array.Empty<MouseBox>(), DefaultButtonMargin, DefaultHoldMs, DefaultDoubleTapMs);

    /// <summary>Lê <c>mouse</c> do JSON do hello, tolerante: campos ausentes ou inválidos usam os padrões. Sem reflexão (trimming/AOT).</summary>
    public static MouseSettings FromHello(JsonElement root)
    {
        if (!root.TryGetProperty("mouse", out var m) || m.ValueKind != JsonValueKind.Object) return Disabled;

        bool enabled = m.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;

        double margin = TryNumber(m, "buttonMargin", out var bm) ? Math.Clamp(bm, 0.0, 0.2) : DefaultButtonMargin;
        int hold = TryNumber(m, "holdMs", out var hm) ? (int)Math.Clamp(Math.Round(hm), 150, 1500) : DefaultHoldMs;
        int doubleTap = TryNumber(m, "doubleTapMs", out var dm) ? (int)Math.Clamp(Math.Round(dm), 100, 800) : DefaultDoubleTapMs;

        var boxes = new List<MouseBox>();
        if (m.TryGetProperty("exclude", out var ex) && ex.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ex.EnumerateArray())
            {
                if (boxes.Count >= MaxBoxes) break;
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!TryNumber(item, "x", out var x) || !TryNumber(item, "y", out var y) ||
                    !TryNumber(item, "w", out var w) || !TryNumber(item, "h", out var h)) continue;

                // Recorta para dentro de 0..1 sem alterar valores que já estão dentro (nada de 0.7 + 0.3 = 0.9999...).
                double x0 = Math.Clamp(x, 0, 1), y0 = Math.Clamp(y, 0, 1);
                double w2 = x < 0 ? Math.Min(x + w, 1) : Math.Min(w, 1 - x0);
                double h2 = y < 0 ? Math.Min(y + h, 1) : Math.Min(h, 1 - y0);
                if (w2 > 0 && h2 > 0) boxes.Add(new MouseBox(x0, y0, w2, h2)); // caixa sem área é descartada
            }
        }

        return new MouseSettings(enabled, boxes, margin, hold, doubleTap);
    }

    private static bool TryNumber(JsonElement parent, string name, out double value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number &&
               p.TryGetDouble(out value) && double.IsFinite(value);
    }
}
