using Android.Content;
using Android.Graphics;
using Android.Views;
using GameStream.Client.Models;

namespace GameStream.Client.Ui;

/// <summary>
/// Botões na tela (botão simples e dpad) com multitoque.
/// Posição (X, Y) = centro, como fração da tela; Size = diâmetro como fração da menor dimensão.
/// No dpad a ordem das teclas é: cima, esquerda, baixo, direita.
/// </summary>
public class ControlsView : View
{
    private readonly Paint _fill = new(PaintFlags.AntiAlias);
    private readonly Paint _stroke = new(PaintFlags.AntiAlias);
    private readonly Paint _text = new(PaintFlags.AntiAlias);
    private readonly float _density;

    private List<ButtonLayout> _layout = new();
    private HashSet<string> _pressed = new();

    /// <summary>(tecla, apertou?) — chamado a cada mudança de estado de uma tecla.</summary>
    public Action<string, bool>? KeyChanged { get; set; }

    public ControlsView(Context context) : base(context)
    {
        _density = context.Resources!.DisplayMetrics!.Density;

        _stroke.SetStyle(Paint.Style.Stroke);
        _stroke.StrokeWidth = 2f * _density;
        _stroke.Color = Color.Argb(150, 255, 255, 255);

        _fill.SetStyle(Paint.Style.Fill);

        _text.Color = Color.Argb(230, 255, 255, 255);
        _text.TextAlign = Paint.Align.Center;
    }

    public void SetLayout(List<ButtonLayout> layout)
    {
        _layout = layout;
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float unit = Math.Min(Width, Height);

        foreach (var c in _layout)
        {
            float cx = (float)c.X * Width;
            float cy = (float)c.Y * Height;
            float r = (float)c.Size * unit / 2f;

            if (c.Type == "dpad") DrawDpad(canvas, c, cx, cy, r);
            else DrawButton(canvas, c, cx, cy, r);
        }
    }

    private void DrawButton(Canvas canvas, ButtonLayout c, float cx, float cy, float r)
    {
        bool down = c.Key is not null && _pressed.Contains(c.Key);
        _fill.Color = Color.Argb(down ? 150 : 60, 255, 255, 255);
        canvas.DrawCircle(cx, cy, r, _fill);
        canvas.DrawCircle(cx, cy, r, _stroke);

        _text.TextSize = Math.Max(12f * _density, r * 0.7f);
        canvas.DrawText(c.Label ?? c.Key ?? "", cx, cy + _text.TextSize * 0.35f, _text);
    }

    private void DrawDpad(Canvas canvas, ButtonLayout c, float cx, float cy, float r)
    {
        _fill.Color = Color.Argb(45, 255, 255, 255);
        canvas.DrawCircle(cx, cy, r, _fill);
        canvas.DrawCircle(cx, cy, r, _stroke);

        if (c.Keys is not { Count: >= 4 } keys) return;

        // cima, esquerda, baixo, direita
        (float dx, float dy)[] offsets = { (0f, -0.62f), (-0.62f, 0f), (0f, 0.62f), (0.62f, 0f) };
        for (int i = 0; i < 4; i++)
        {
            _fill.Color = Color.Argb(_pressed.Contains(keys[i]) ? 190 : 80, 255, 255, 255);
            canvas.DrawCircle(cx + offsets[i].dx * r, cy + offsets[i].dy * r, r * 0.2f, _fill);
        }
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;

        var next = new HashSet<string>();
        var action = e.ActionMasked;
        bool lifting = action == MotionEventActions.Up || action == MotionEventActions.PointerUp;

        if (action != MotionEventActions.Cancel)
        {
            for (int i = 0; i < e.PointerCount; i++)
            {
                if (lifting && i == e.ActionIndex) continue; // esse dedo acabou de sair
                Collect(e.GetX(i), e.GetY(i), next);
            }
        }

        Apply(next);
        return true;
    }

    /// <summary>Descobre quais teclas o toque em (x, y) aciona.</summary>
    private void Collect(float x, float y, HashSet<string> keys)
    {
        float unit = Math.Min(Width, Height);

        foreach (var c in _layout)
        {
            float cx = (float)c.X * Width;
            float cy = (float)c.Y * Height;
            float r = (float)c.Size * unit / 2f;
            float dx = x - cx, dy = y - cy;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (c.Type == "dpad")
            {
                if (c.Keys is not { Count: >= 4 } k || dist > r * 1.6f) continue;

                float nx = dx / r, ny = dy / r;
                if (MathF.Sqrt(nx * nx + ny * ny) < 0.2f) continue; // zona morta no centro

                const float t = 0.35f;
                if (ny < -t) keys.Add(k[0]);
                if (nx < -t) keys.Add(k[1]);
                if (ny > t) keys.Add(k[2]);
                if (nx > t) keys.Add(k[3]);
            }
            else if (c.Key is not null && dist <= r * 1.2f)
            {
                keys.Add(c.Key);
            }
        }
    }

    private void Apply(HashSet<string> next)
    {
        bool pressedNew = false;

        foreach (var k in _pressed)
            if (!next.Contains(k)) KeyChanged?.Invoke(k, false);

        foreach (var k in next)
            if (!_pressed.Contains(k))
            {
                KeyChanged?.Invoke(k, true);
                pressedNew = true;
            }

        bool changed = pressedNew || !_pressed.SetEquals(next);
        _pressed = next;

        if (pressedNew) PerformHapticFeedback(FeedbackConstants.VirtualKey);
        if (changed) Invalidate();
    }

    /// <summary>Solta tudo (ex.: ao sair da tela), para o jogo não ficar com tecla presa.</summary>
    public void ReleaseAll() => Apply(new HashSet<string>());
}
