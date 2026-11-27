using GameStream.Client.Models;

namespace GameStream.Client.Ui;

/// <summary>
/// Área de toque dos botões e do joystick (sem dependência do Android, para ser testada). É a MESMA geometria que a
/// <see cref="ControlsView"/> usa para apertar teclas, então "este dedo é de um botão?" e "este dedo aperta um botão?"
/// nunca discordam.
/// </summary>
public static class ButtonHitArea
{
    /// <summary>Um botão aceita toque até 1,2 vez o raio desenhado.</summary>
    public const float ButtonReach = 1.2f;

    /// <summary>O joystick aceita toque até 1,6 vez o raio desenhado.</summary>
    public const float JoystickReach = 1.6f;

    /// <summary>
    /// true se (x, y) cai na área de toque de algum botão ou joystick do layout, mais uma folga de <paramref name="marginPx"/>
    /// em volta (todos os lados). <paramref name="width"/> e <paramref name="height"/> são o tamanho da tela em pixels.
    /// </summary>
    public static bool Hits(IEnumerable<ButtonLayout> layout, float width, float height, float x, float y, float marginPx)
    {
        float unit = Math.Min(width, height);

        foreach (var c in layout)
        {
            bool joystick = c.Type == "dpad";
            if (!joystick && c.Key is null) continue; // botão sem tecla não aciona nada

            float cx = (float)c.X * width;
            float cy = (float)c.Y * height;
            float r = (float)c.Size * unit / 2f;
            float reach = r * (joystick ? JoystickReach : ButtonReach) + marginPx;
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= reach * reach) return true;
        }
        return false;
    }
}
