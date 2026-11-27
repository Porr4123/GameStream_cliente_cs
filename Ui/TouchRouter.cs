using Android.Content;
using Android.Views;
using Android.Widget;

namespace GameStream.Client.Ui;

/// <summary>
/// Pai das duas camadas de toque da tela do jogo. Decide, no momento em que CADA dedo desce, se ele é dos botões
/// (<see cref="ControlsView"/>) ou do mouse (<see cref="MouseLayerView"/>), e esse dono vale até o dedo levantar:
///  - o dedo que começou num botão (ou na folga em volta dele, ou numa caixa de exclusão) é dos botões, mesmo se arrastar;
///  - o dedo que começou fora é do mouse, mesmo se passar por cima de um botão.
///
/// Por que não deixar o Android despachar? Quando a ControlsView já está segurando um dedo, um segundo dedo que cai nela
/// é entregue direto a ela, sem chance de recusar e passar para a camada de baixo, e "um dedo no botão e outro no mouse"
/// deixaria de funcionar. Aqui cada dedo é decidido separadamente.
/// </summary>
public class TouchRouter : FrameLayout
{
    private readonly PointerOwnership _owners;

    public ControlsView? Controls { get; set; }
    public MouseLayerView? Mouse { get; set; }

    public TouchRouter(Context context) : base(context)
    {
        _owners = new PointerOwnership(Decide);
    }

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        var controls = Controls;
        if (e is null || controls is null) return base.DispatchTouchEvent(e);

        var action = e.ActionMasked;
        if (action == MotionEventActions.Down || action == MotionEventActions.PointerDown)
        {
            int index = e.ActionIndex;
            _owners.Down(e.GetPointerId(index), e.GetX(index), e.GetY(index));
        }

        controls.HandleTouch(e, _owners.OwnsControls);
        Mouse?.HandleTouch(e, _owners.OwnsMouse);

        if (action == MotionEventActions.Up || action == MotionEventActions.Cancel) _owners.Clear();
        else if (action == MotionEventActions.PointerUp) _owners.Up(e.GetPointerId(e.ActionIndex));
        return true;
    }

    private PointerOwner Decide(float x, float y)
    {
        var mouse = Mouse;
        var controls = Controls;
        if (mouse is null || controls is null || !mouse.Active) return PointerOwner.Controls; // sem mouse: tudo como antes
        return PointerOwnership.Decide(
            mouseActive: true,
            hitsButton: controls.HitsAnyButton(x, y, mouse.ButtonMarginPx), // botão + folga
            excluded: mouse.IsExcluded(x, y),                               // caixa de exclusão do perfil
            insideVideo: mouse.ContainsPoint(x, y));
    }
}
