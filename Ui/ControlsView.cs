using Android.Content;
using Android.Graphics;
using Android.Views;
using GameStream.Client.Models;

namespace GameStream.Client.Ui;

/// <summary>
/// Botões na tela (botão simples e joystick) com multitoque.
/// Posição (X, Y) = centro, como fração da tela; Size = diâmetro como fração da menor dimensão.
/// No joystick (Type "dpad") a ordem das teclas é: cima, esquerda, baixo, direita.
///
/// Com <see cref="EditMode"/> ligado a view vira um editor: tocar seleciona um controle, arrastar move,
/// e nada é enviado ao servidor.
/// </summary>
public class ControlsView : View
{
    private readonly Paint _fill = new(PaintFlags.AntiAlias);
    private readonly Paint _stroke = new(PaintFlags.AntiAlias);
    private readonly Paint _text = new(PaintFlags.AntiAlias);
    private readonly Paint _select = new(PaintFlags.AntiAlias);
    private readonly float _density;

    private List<ButtonLayout> _layout = new();
    private HashSet<string> _pressed = new();
    private Dictionary<ButtonLayout, (float X, float Y)> _knobs = new();

    private bool _dragging;
    private float _dragDx, _dragDy;

    /// <summary>(tecla, apertou?) — chamado a cada mudança de estado de uma tecla.</summary>
    public Action<string, bool>? KeyChanged { get; set; }

    /// <summary>Editor: ligado, a view tem proporção 16:9, arrasta controles e não envia teclas.</summary>
    public bool EditMode { get; set; }

    /// <summary>Editor: índice do controle selecionado (-1 = nenhum).</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Editor: o usuário tocou em um controle (ou em área vazia = -1).</summary>
    public Action<int>? SelectionChanged { get; set; }

    /// <summary>Editor: o usuário terminou de arrastar um controle.</summary>
    public Action? LayoutEdited { get; set; }

    public ControlsView(Context context) : base(context)
    {
        _density = context.Resources!.DisplayMetrics!.Density;

        _stroke.SetStyle(Paint.Style.Stroke);
        _stroke.StrokeWidth = 2f * _density;
        _stroke.Color = Color.Argb(150, 255, 255, 255);

        _fill.SetStyle(Paint.Style.Fill);

        _text.TextAlign = Paint.Align.Center;

        _select.SetStyle(Paint.Style.Stroke);
        _select.StrokeWidth = 3f * _density;
        _select.Color = UiKit.Accent;
    }

    public void SetLayout(List<ButtonLayout> layout)
    {
        _layout = layout;
        SelectedIndex = -1;
        Invalidate();
    }

    /// <summary>Editor: seleciona um controle pelo índice.</summary>
    public void Select(int index)
    {
        SelectedIndex = index;
        Invalidate();
    }

    /// <summary>Os dados do layout mudaram por fora (tecla, texto, tamanho): redesenha.</summary>
    public void Refresh() => Invalidate();

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        if (!EditMode)
        {
            base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
            return;
        }
        int w = MeasureSpec.GetSize(widthMeasureSpec);
        SetMeasuredDimension(w, w * 9 / 16); // mesma proporção de um celular deitado
    }

    // ---------------------------------------------------------------- desenho

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        if (EditMode)
        {
            _fill.Color = Color.Argb(255, 24, 27, 35);
            canvas.DrawRect(0, 0, Width, Height, _fill);
            canvas.DrawRect(1, 1, Width - 1, Height - 1, _stroke);
        }

        float unit = Math.Min(Width, Height);
        for (int i = 0; i < _layout.Count; i++)
        {
            var c = _layout[i];
            float cx = (float)c.X * Width;
            float cy = (float)c.Y * Height;
            float r = (float)c.Size * unit / 2f;

            if (c.Type == "dpad") DrawJoystick(canvas, c, cx, cy, r);
            else DrawButton(canvas, c, cx, cy, r);

            if (EditMode && i == SelectedIndex) canvas.DrawCircle(cx, cy, r * 1.18f, _select);
        }
    }

    private void DrawButton(Canvas canvas, ButtonLayout c, float cx, float cy, float r)
    {
        bool down = c.Key is not null && _pressed.Contains(c.Key);
        _fill.Color = Color.Argb(down ? 150 : 60, 255, 255, 255);
        canvas.DrawCircle(cx, cy, r, _fill);
        canvas.DrawCircle(cx, cy, r, _stroke);

        string label = string.IsNullOrWhiteSpace(c.Label) ? KeyCatalog.Display(c.Key) : c.Label!;
        // Textos mais longos (Shift, Ctrl) ficam menores para caber no círculo.
        float factor = label.Length <= 2 ? 0.7f : Math.Max(0.35f, 1.5f / label.Length);
        _text.Color = Color.Argb(230, 255, 255, 255);
        _text.TextSize = Math.Max(9f * _density, r * factor);
        canvas.DrawText(label, cx, cy + _text.TextSize * 0.35f, _text);
    }

    private void DrawJoystick(Canvas canvas, ButtonLayout c, float cx, float cy, float r)
    {
        _fill.Color = Color.Argb(45, 255, 255, 255);
        canvas.DrawCircle(cx, cy, r, _fill);
        canvas.DrawCircle(cx, cy, r, _stroke);

        if (c.Keys is not { Count: >= 4 } keys) return;

        // cima, esquerda, baixo, direita: a tecla de cada direção aparece na borda do joystick
        (float dx, float dy)[] offsets = { (0f, -0.72f), (-0.72f, 0f), (0f, 0.72f), (0.72f, 0f) };
        _text.TextSize = Math.Max(9f * _density, r * 0.22f);
        for (int i = 0; i < 4; i++)
        {
            _text.Color = Color.Argb(_pressed.Contains(keys[i]) ? 255 : 150, 255, 255, 255);
            canvas.DrawText(KeyCatalog.Display(keys[i]), cx + offsets[i].dx * r, cy + offsets[i].dy * r + _text.TextSize * 0.35f, _text);
        }

        // bolinha central acompanha o dedo
        _knobs.TryGetValue(c, out var knob);
        bool active = knob.X != 0f || knob.Y != 0f;
        _fill.Color = Color.Argb(active ? 170 : 90, 255, 255, 255);
        canvas.DrawCircle(cx + knob.X * r * 0.45f, cy + knob.Y * r * 0.45f, r * 0.30f, _fill);
    }

    // ---------------------------------------------------------------- toque

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;
        if (EditMode) return HandleEditTouch(e);
        return HandleTouch(e, null);
    }

    /// <summary>
    /// Trata um evento de toque considerando só os dedos que esta view possui. <paramref name="owns"/> recebe o id do dedo;
    /// null = todos. A <see cref="TouchRouter"/> usa isto para dar cada dedo à camada de botões ou à de mouse.
    /// </summary>
    public bool HandleTouch(MotionEvent e, Func<int, bool>? owns)
    {
        var next = new HashSet<string>();
        var knobs = new Dictionary<ButtonLayout, (float X, float Y)>();
        var action = e.ActionMasked;
        bool lifting = action == MotionEventActions.Up || action == MotionEventActions.PointerUp;

        if (action != MotionEventActions.Cancel)
        {
            for (int i = 0; i < e.PointerCount; i++)
            {
                if (lifting && i == e.ActionIndex) continue; // esse dedo acabou de sair
                if (owns != null && !owns(e.GetPointerId(i))) continue;
                Collect(e.GetX(i), e.GetY(i), next, knobs);
            }
        }

        _knobs = knobs;
        Apply(next);
        Invalidate(); // a bolinha do joystick se move mesmo sem mudar de tecla
        return true;
    }

    /// <summary>
    /// true se (x, y) cai na área de toque de algum botão ou joystick (a área real de <see cref="Collect"/>), mais uma
    /// folga de <paramref name="marginPx"/> em volta. Usado para decidir se o dedo é dos botões ou do mouse.
    /// </summary>
    public bool HitsAnyButton(float x, float y, float marginPx) =>
        ButtonHitArea.Hits(_layout, Width, Height, x, y, marginPx);

    /// <summary>Descobre quais teclas o toque em (x, y) aciona.</summary>
    private void Collect(float x, float y, HashSet<string> keys, Dictionary<ButtonLayout, (float X, float Y)> knobs)
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
                if (c.Keys is not { Count: >= 4 } k || dist > r * ButtonHitArea.JoystickReach) continue;

                float nx = dx / r, ny = dy / r;
                float mag = MathF.Sqrt(nx * nx + ny * ny);
                knobs[c] = mag > 1f ? (nx / mag, ny / mag) : (nx, ny);
                if (mag < 0.2f) continue; // zona morta no centro

                const float t = 0.35f;
                if (ny < -t) keys.Add(k[0]);
                if (nx < -t) keys.Add(k[1]);
                if (ny > t) keys.Add(k[2]);
                if (nx > t) keys.Add(k[3]);
            }
            else if (c.Key is not null && dist <= r * ButtonHitArea.ButtonReach)
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

        _pressed = next;
        if (pressedNew) PerformHapticFeedback(FeedbackConstants.VirtualKey);
    }

    /// <summary>Solta tudo (ex.: ao sair da tela), para o jogo não ficar com tecla presa.</summary>
    public void ReleaseAll()
    {
        _knobs = new Dictionary<ButtonLayout, (float X, float Y)>();
        Apply(new HashSet<string>());
    }

    // ---------------------------------------------------------------- editor

    private bool HandleEditTouch(MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            {
                Parent?.RequestDisallowInterceptTouchEvent(true); // a rolagem da tela não rouba o arrasto
                int hit = HitTest(e.GetX(), e.GetY());
                SelectedIndex = hit;
                if (hit >= 0)
                {
                    var c = _layout[hit];
                    _dragDx = (float)c.X * Width - e.GetX();
                    _dragDy = (float)c.Y * Height - e.GetY();
                    _dragging = true;
                }
                SelectionChanged?.Invoke(hit);
                Invalidate();
                return true;
            }
            case MotionEventActions.Move:
                if (_dragging && SelectedIndex >= 0 && SelectedIndex < _layout.Count && Width > 0 && Height > 0)
                {
                    var c = _layout[SelectedIndex];
                    c.X = Math.Clamp((e.GetX() + _dragDx) / Width, 0.03, 0.97);
                    c.Y = Math.Clamp((e.GetY() + _dragDy) / Height, 0.03, 0.97);
                    Invalidate();
                }
                return true;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_dragging)
                {
                    _dragging = false;
                    LayoutEdited?.Invoke();
                }
                return true;
        }
        return true;
    }

    /// <summary>Controle mais ao topo sob (x, y), ou -1. Área de toque mínima de 20 dp.</summary>
    private int HitTest(float x, float y)
    {
        float unit = Math.Min(Width, Height);
        for (int i = _layout.Count - 1; i >= 0; i--)
        {
            var c = _layout[i];
            float cx = (float)c.X * Width;
            float cy = (float)c.Y * Height;
            float r = (float)c.Size * unit / 2f;
            float reach = Math.Max(r * 1.1f, 20f * _density);
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= reach * reach) return i;
        }
        return -1;
    }
}
