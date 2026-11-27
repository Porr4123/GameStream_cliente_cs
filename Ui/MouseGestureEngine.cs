namespace GameStream.Client.Ui;

/// <summary>
/// Gestos de toque -> mouse, sem dependência do Android (para poder ser testado com relógio falso).
///
///  - toque rápido, sem andar: clique ESQUERDO;
///  - 2 toques rápidos (o 2º em até DoubleTapMs do 1º, perto dele): clique DIREITO. É a opção (b), latência zero: o 1º toque
///    já enviou o clique esquerdo na hora, então no duplo toque o servidor recebe ESQUERDO e depois DIREITO (o 2º toque envia
///    só o direito);
///  - dedo parado por HoldMs: "down" esquerdo (clique contínuo); andar = "move"; levantar = "up" esquerdo (arrastar);
///  - andar mais que o slop antes de HoldMs: só "move" (o cursor acompanha o dedo, sem clicar).
///
/// Sempre que o dedo desce manda um "move" antes. Só um dedo comanda o mouse por vez (os outros nessa camada são ignorados),
/// e todo "down" enviado termina em "up" (levantar, cancelar ou <see cref="Cancel"/>).
/// </summary>
public sealed class MouseGestureEngine
{
    public delegate void Sender(string action, string button, double x, double y);

    /// <summary>Intervalo mínimo entre "move" (~60 por segundo; o servidor aceita até ~120).</summary>
    public const long MinMoveIntervalMs = 16;

    private enum State { Idle, Pending, Moving, Holding }

    private readonly Sender _send;
    private readonly Func<float, float, (double X, double Y)> _normalize;

    private State _state = State.Idle;
    private int _id;
    private float _downX, _downY, _curX, _curY;
    private long _downMs;

    private long _lastMoveMs = long.MinValue / 2;
    private float _lastMoveX = float.NaN, _lastMoveY = float.NaN;

    private bool _lastTapValid;
    private long _lastTapUpMs;
    private float _lastTapX, _lastTapY;

    /// <param name="normalize">Converte um ponto em pixels da tela para 0..1 no quadro de vídeo.</param>
    public MouseGestureEngine(Sender send, Func<float, float, (double X, double Y)> normalize)
    {
        _send = send;
        _normalize = normalize;
    }

    public int HoldMs { get; set; } = 350;
    public int DoubleTapMs { get; set; } = 300;
    public float TouchSlopPx { get; set; } = 8f;
    public float DoubleTapSlopPx { get; set; } = 100f;

    /// <summary>O "segurar" foi reconhecido (a interface dá uma vibração curta).</summary>
    public event Action? HoldRecognized;

    public bool IsActive => _state != State.Idle;
    public bool IsHolding => _state == State.Holding;
    public int PointerId => _id;
    public float CurrentX => _curX;
    public float CurrentY => _curY;

    /// <summary>Quando (mesmo relógio dos eventos) o "segurar" deve ser reconhecido, se o dedo continuar parado. null = nenhum.</summary>
    public long? HoldDeadlineMs => _state == State.Pending ? _downMs + HoldMs : null;

    /// <summary>Um dedo desceu nesta camada. Devolve false se outro dedo já comanda o mouse (este é ignorado).</summary>
    public bool PointerDown(int id, float x, float y, long nowMs)
    {
        if (_state != State.Idle) return false;

        _state = State.Pending;
        _id = id;
        _downX = _curX = x;
        _downY = _curY = y;
        _downMs = nowMs;
        SendMove(x, y, nowMs, force: true); // o jogo precisa saber onde o cursor está antes de qualquer clique
        return true;
    }

    public void PointerMove(int id, float x, float y, long nowMs)
    {
        if (_state == State.Idle || id != _id) return;
        Tick(nowMs);
        _curX = x;
        _curY = y;

        if (_state == State.Pending)
        {
            float dx = x - _downX, dy = y - _downY;
            if (MathF.Sqrt(dx * dx + dy * dy) <= TouchSlopPx) return; // ainda é um toque (ou vai virar "segurar")
            _state = State.Moving;       // andou antes de HoldMs: não é toque nem segurar
            _lastTapValid = false;
        }
        SendMove(x, y, nowMs, force: false);
    }

    public void PointerUp(int id, float x, float y, long nowMs)
    {
        if (_state == State.Idle || id != _id) return;
        Tick(nowMs); // se o relógio atrasou, o "segurar" é reconhecido antes de soltar
        _curX = x;
        _curY = y;

        switch (_state)
        {
            case State.Pending:
                Tap(nowMs);
                break;
            case State.Moving:
                _lastTapValid = false;
                if (x != _lastMoveX || y != _lastMoveY) SendMove(x, y, nowMs, force: true); // posição final, mesmo no limite de taxa
                break;
            case State.Holding:
                _lastTapValid = false;
                Send("up", "left", x, y);
                break;
        }
        _state = State.Idle;
    }

    /// <summary>Marca o tempo: se o dedo está parado há HoldMs, vira "segurar" ("down" esquerdo).</summary>
    public void Tick(long nowMs)
    {
        if (_state != State.Pending || nowMs - _downMs < HoldMs) return;

        _state = State.Holding;
        _lastTapValid = false;
        Send("down", "left", _curX, _curY);
        HoldRecognized?.Invoke();
    }

    /// <summary>Cancelamento do toque, app em segundo plano, conexão caindo, camada desligada: solta o botão se estiver apertado.</summary>
    public void Cancel()
    {
        if (_state == State.Holding) Send("up", "left", _curX, _curY);
        _state = State.Idle;
        _lastTapValid = false;
    }

    private void Tap(long upMs)
    {
        // 2º toque em até DoubleTapMs depois que o 1º soltou, e perto dele = clique direito (só o direito: o esquerdo já foi no 1º)
        bool isDouble = _lastTapValid
                        && _downMs - _lastTapUpMs <= DoubleTapMs
                        && Distance(_downX, _downY, _lastTapX, _lastTapY) <= DoubleTapSlopPx;
        if (isDouble)
        {
            Send("click", "right", _downX, _downY);
            _lastTapValid = false; // um 3º toque começa de novo (esquerdo)
        }
        else
        {
            Send("click", "left", _downX, _downY);
            _lastTapValid = true;
            _lastTapUpMs = upMs;
            _lastTapX = _downX;
            _lastTapY = _downY;
        }
    }

    private void SendMove(float x, float y, long nowMs, bool force)
    {
        if (!force && nowMs - _lastMoveMs < MinMoveIntervalMs) return;
        _lastMoveMs = nowMs;
        _lastMoveX = x;
        _lastMoveY = y;
        Send("move", "left", x, y);
    }

    private void Send(string action, string button, float x, float y)
    {
        var (nx, ny) = _normalize(x, y);
        _send(action, button, nx, ny);
    }

    private static float Distance(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0, dy = y1 - y0;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
