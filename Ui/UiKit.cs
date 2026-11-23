using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Widget;

namespace GameStream.Client.Ui;

public enum BtnStyle { Primary, Secondary, Danger, Chip, ChipSelected }

/// <summary>Cores e pequenos construtores de tela (o app monta a interface por código, sem XML).</summary>
public static class UiKit
{
    public static readonly Color Background = Color.Rgb(18, 20, 26);
    public static readonly Color Surface = Color.Rgb(30, 34, 44);
    public static readonly Color SurfaceHigh = Color.Rgb(46, 52, 66);
    public static readonly Color Accent = Color.Rgb(79, 140, 255);
    public static readonly Color TextMuted = Color.Rgb(154, 163, 178);
    public static readonly Color Warning = Color.Rgb(255, 176, 32);
    public static readonly Color Danger = Color.Rgb(235, 87, 87);

    public static int Dp(Context ctx, float value) => (int)(value * ctx.Resources!.DisplayMetrics!.Density + 0.5f);

    public static GradientDrawable Round(Context ctx, Color fill, float radiusDp, Color? stroke = null)
    {
        var d = new GradientDrawable();
        d.SetColor(fill);
        d.SetCornerRadius(Dp(ctx, radiusDp));
        if (stroke is Color s) d.SetStroke(Dp(ctx, 1), s);
        return d;
    }

    public static TextView Label(Context ctx, string text, float sizeSp, Color color, bool bold = false)
    {
        var tv = new TextView(ctx) { Text = text, TextSize = sizeSp };
        tv.SetTextColor(color);
        if (bold) tv.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Normal);
        return tv;
    }

    public static Button MakeButton(Context ctx, string text, BtnStyle style)
    {
        var b = new Button(ctx) { Text = text };
        b.SetAllCaps(false);
        b.StateListAnimator = null;
        b.MinimumHeight = Dp(ctx, 44);
        b.MinimumWidth = 0;

        Color fill, textColor = Color.White;
        Color? stroke = null;
        switch (style)
        {
            case BtnStyle.Primary: fill = Accent; break;
            case BtnStyle.Danger: fill = Surface; textColor = Danger; stroke = Danger; break;
            case BtnStyle.ChipSelected: fill = Accent; break;
            case BtnStyle.Chip: fill = SurfaceHigh; break;
            default: fill = SurfaceHigh; break;
        }
        b.SetTextColor(textColor);
        b.Background = Round(ctx, fill, (style is BtnStyle.Chip or BtnStyle.ChipSelected) ? 22 : 10, stroke);
        b.SetPadding(Dp(ctx, 16), Dp(ctx, 6), Dp(ctx, 16), Dp(ctx, 6));
        return b;
    }

    public static EditText Input(Context ctx, string hint, string value, InputTypes type)
    {
        var e = new EditText(ctx) { Hint = hint, Text = value, InputType = type };
        e.SetSingleLine(true);
        e.SetTextColor(Color.White);
        e.SetHintTextColor(TextMuted);
        e.Background = Round(ctx, SurfaceHigh, 10);
        e.SetPadding(Dp(ctx, 14), Dp(ctx, 12), Dp(ctx, 14), Dp(ctx, 12));
        return e;
    }

    public static LinearLayout Card(Context ctx)
    {
        var card = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        card.Background = Round(ctx, Surface, 16);
        int p = Dp(ctx, 16);
        card.SetPadding(p, p, p, p);
        return card;
    }
}
