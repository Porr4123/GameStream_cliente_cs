namespace GameStream.Client.Ui;

public enum PointerOwner { Controls, Mouse }

/// <summary>
/// De quem é cada dedo (sem dependência do Android, para ser testada): decidido UMA vez, quando o dedo desce, e vale até
/// ele levantar. Assim o dedo que começou num botão é do botão mesmo se arrastar para fora, e o que começou fora é do
/// mouse mesmo se passar por cima de um botão. Cada dedo é decidido separadamente (multitoque).
/// </summary>
public sealed class PointerOwnership
{
    private readonly Dictionary<int, PointerOwner> _owners = new();
    private readonly Func<float, float, PointerOwner> _decide;

    /// <param name="decide">Quem fica com um dedo que desceu em (x, y), em pixels.</param>
    public PointerOwnership(Func<float, float, PointerOwner> decide) => _decide = decide;

    /// <summary>O dedo <paramref name="id"/> desceu em (x, y): decide o dono dele.</summary>
    public PointerOwner Down(int id, float x, float y) => _owners[id] = _decide(x, y);

    /// <summary>Dedo sem dono registrado (ex.: evento que começou antes de a tela existir) fica com os botões, como sempre foi.</summary>
    public bool OwnsControls(int id) => !_owners.TryGetValue(id, out var o) || o == PointerOwner.Controls;

    public bool OwnsMouse(int id) => _owners.TryGetValue(id, out var o) && o == PointerOwner.Mouse;

    public void Up(int id) => _owners.Remove(id);

    public void Clear() => _owners.Clear();

    /// <summary>
    /// Regra de decisão de um dedo novo: com a camada de mouse ligada, só vai para o mouse se NÃO cair num botão (+ folga),
    /// nem numa caixa de exclusão, e cair dentro da área do vídeo (fora dela não há onde clicar).
    /// </summary>
    public static PointerOwner Decide(bool mouseActive, bool hitsButton, bool excluded, bool insideVideo) =>
        mouseActive && !hitsButton && !excluded && insideVideo ? PointerOwner.Mouse : PointerOwner.Controls;
}
