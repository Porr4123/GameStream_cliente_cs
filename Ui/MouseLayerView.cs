using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using GameStream.Client.Models;

namespace GameStream.Client.Ui;

/// <summary>
/// Camada 1 (logo acima do vídeo): converte os toques que NÃO são dos botões em mouse (toque = clique esquerdo, duplo toque =
/// clique direito, segurar = arrastar com o esquerdo). Os gestos ficam em <see cref="MouseGestureEngine"/>.
///
/// Quem entrega os dedos a esta view é a <see cref="TouchRouter"/>, que decide o dono de cada dedo quando ele desce.
/// As coordenadas enviadas são relativas à área do <see cref="VideoView"/> (0..1), incluindo as faixas pretas do quadro:
/// quem descarta toque em faixa preta é o servidor.
/// </summary>
public class MouseLayerView : View
{
    private readonly MouseGestureEngine _engine;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Java.Lang.IRunnable _tick;
    private readonly Paint _fill = new(PaintFlags.AntiAlias);
    private readonly Paint _ring = new(PaintFlags.AntiAlias);
    private readonly float _density;

    /// <summary>A view do vídeo: as coordenadas 0..1 são relativas à área dela.</summary>
    public View? VideoView { get; set; }

    /// <summary>Envia um evento de mouse (ação, botão, x, y). Ligado ao <c>StreamClient.SendMouse</c>.</summary>
    public Action<string, string, double, double>? Sender { get; set; }

    public MouseSettings Settings { get; private set; } = MouseSettings.Disabled;

    /// <summary>Escolha do usuário no perfil (o servidor também precisa ter mandado <c>enabled: true</c>).</summary>
    public bool UserEnabled { get; private set; } = true;

    public MouseLayerView(Context context) : base(context)
    {
        _density = context.Resources!.DisplayMetrics!.Density;

        _engine = new MouseGestureEngine((a, b, x, y) => Sender?.Invoke(a, b, x, y), Normalize);
        var config = ViewConfiguration.Get(context)!;
        _engine.TouchSlopPx = config.ScaledTouchSlop;
        _engine.DoubleTapSlopPx = config.ScaledDoubleTapSlop;
        _engine.HoldRecognized += () => PerformHapticFeedback(FeedbackConstants.LongPress); // vibração curta ao reconhecer o "segurar"

        _tick = new Java.Lang.Runnable(() =>
        {
            _engine.Tick(SystemClock.UptimeMillis());
            Invalidate();
        });

        _fill.SetStyle(Paint.Style.Fill);
        _ring.SetStyle(Paint.Style.Stroke);
        _ring.StrokeWidth = 2f * _density;
    }

    /// <summary>Aplica a configuração do servidor e a escolha do usuário. Desligar solta qualquer botão apertado.</summary>
    public void Configure(MouseSettings settings, bool userEnabled)
    {
        Settings = settings;
        UserEnabled = userEnabled;
        _engine.HoldMs = settings.HoldMs;
        _engine.DoubleTapMs = settings.DoubleTapMs;
        if (!Active) ReleaseAll();
        Invalidate();
    }

    /// <summary>true se a camada de mouse está valendo (servidor ligou, usuário não desligou e o vídeo já tem área).</summary>
    public bool Active => UserEnabled && Settings.Enabled && TryGetVideoRect(out _, out _, out _, out _);

    /// <summary>Folga em volta dos botões, em pixels: fração do lado menor da tela.</summary>
    public float ButtonMarginPx => (float)(Settings.ButtonMargin * Math.Min(Width, Height));

    private bool TryGetVideoRect(out float left, out float top, out float width, out float height)
    {
        var v = VideoView;
        if (v is null || v.Width <= 0 || v.Height <= 0)
        {
            left = top = width = height = 0;
            return false;
        }
        left = v.Left;
        top = v.Top;
        width = v.Width;
        height = v.Height;
        return true;
    }

    /// <summary>Pixels da tela -> 0..1 no quadro de vídeo.</summary>
    private (double X, double Y) Normalize(float x, float y) =>
        TryGetVideoRect(out var l, out var t, out var w, out var h) ? ((x - l) / w, (y - t) / h) : (0.0, 0.0);

    /// <summary>O ponto está dentro da área do vídeo?</summary>
    public bool ContainsPoint(float x, float y) =>
        TryGetVideoRect(out var l, out var t, out var w, out var h) && x >= l && x <= l + w && y >= t && y <= t + h;

    /// <summary>O ponto está numa caixa de exclusão do perfil (<c>mouse.exclude</c>)? Aí o toque é dos botões.</summary>
    public bool IsExcluded(float x, float y)
    {
        if (!TryGetVideoRect(out var l, out var t, out var w, out var h)) return false;
        foreach (var b in Settings.Exclude)
        {
            float bx = l + (float)b.X * w, by = t + (float)b.Y * h;
            if (x >= bx && x <= bx + (float)b.W * w && y >= by && y <= by + (float)b.H * h) return true;
        }
        return false;
    }

    /// <summary>Trata um evento de toque considerando só os dedos que esta camada possui.</summary>
    public void HandleTouch(MotionEvent e, Func<int, bool> owns)
    {
        long now = e.EventTime; // mesmo relógio de SystemClock.UptimeMillis()
        _engine.Tick(now);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
            {
                int index = e.ActionIndex;
                int id = e.GetPointerId(index);
                if (owns(id) && _engine.PointerDown(id, e.GetX(index), e.GetY(index), now)) ScheduleHold();
                break;
            }
            case MotionEventActions.Move:
                for (int i = 0; i < e.PointerCount; i++)
                {
                    int id = e.GetPointerId(i);
                    if (owns(id)) _engine.PointerMove(id, e.GetX(i), e.GetY(i), now);
                }
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
            {
                int index = e.ActionIndex;
                int id = e.GetPointerId(index);
                if (owns(id))
                {
                    _engine.PointerUp(id, e.GetX(index), e.GetY(index), now);
                    _handler.RemoveCallbacks(_tick);
                }
                break;
            }
            case MotionEventActions.Cancel:
                ReleaseAll();
                break;
        }
        Invalidate();
    }

    private void ScheduleHold()
    {
        _handler.RemoveCallbacks(_tick);
        if (_engine.HoldDeadlineMs is long deadline)
            _handler.PostDelayed(_tick, Math.Max(1L, deadline - SystemClock.UptimeMillis()));
    }

    /// <summary>
    /// Solta tudo (ex.: ACTION_CANCEL, app em segundo plano, conexão caindo): se um "down" foi enviado, manda o "up".
    /// Nenhum botão do mouse pode ficar preso.
    /// </summary>
    public void ReleaseAll()
    {
        _handler.RemoveCallbacks(_tick);
        _engine.Cancel();
        Invalidate();
    }

    protected override void OnDetachedFromWindow()
    {
        _handler.RemoveCallbacks(_tick);
        base.OnDetachedFromWindow();
    }

    /// <summary>Indicador discreto: um círculo onde o dedo está, mais forte quando o botão esquerdo está apertado.</summary>
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (!_engine.IsActive) return;

        bool holding = _engine.IsHolding;
        _fill.Color = Color.Argb(holding ? 90 : 40, 255, 255, 255);
        _ring.Color = Color.Argb(holding ? 230 : 120, 255, 255, 255);
        float r = (holding ? 26f : 20f) * _density;
        canvas.DrawCircle(_engine.CurrentX, _engine.CurrentY, r, _fill);
        canvas.DrawCircle(_engine.CurrentX, _engine.CurrentY, r, _ring);
    }
}
