using System.Text.Json.Serialization;

namespace GameStream.Client.Models;

/// <summary>Um item de GET /games (mesmo formato de GameProfile no servidor, em camelCase).</summary>
public sealed class GameInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int StreamWidth { get; set; }
    public int StreamHeight { get; set; }
    public int Fps { get; set; }
    /// <summary>Teclas que o layout padrão do jogo usa. No servidor atual é só informativa (não decide o que é injetado).</summary>
    public List<string> AllowedKeys { get; set; } = new();

    /// <summary>
    /// Lista fixa de teclas que o servidor realmente injeta (letras, números, setas, Ctrl, Shift e Esc). Servidores antigos
    /// não mandam este campo (fica nulo); nesses, quem decide é <see cref="AllowedKeys"/>.
    /// </summary>
    public List<string>? SafeKeys { get; set; }

    public List<ButtonLayout> Layout { get; set; } = new();

    /// <summary>true se o servidor informou a lista de teclas seguras (servidor atual).</summary>
    public bool HasSafeKeys => SafeKeys is { Count: > 0 };

    /// <summary>As teclas que o servidor aceita para este jogo, em maiúsculas e sem diferenciar caixa.</summary>
    public HashSet<string> UsableKeys() =>
        new(HasSafeKeys ? SafeKeys! : AllowedKeys, StringComparer.OrdinalIgnoreCase);
}

public sealed class ButtonLayout
{
    /// <summary>"button" (uma tecla) ou "dpad" (4 teclas: cima, esquerda, baixo, direita).</summary>
    public string Type { get; set; } = "button";
    public string? Key { get; set; }
    public List<string>? Keys { get; set; }
    public string? Label { get; set; }

    /// <summary>Centro do controle, como fração da tela (0 a 1).</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Diâmetro como fração da menor dimensão da tela.</summary>
    public double Size { get; set; } = 0.12;

    public ButtonLayout Clone() => new()
    {
        Type = Type,
        Key = Key,
        Keys = Keys is null ? null : new List<string>(Keys),
        Label = Label,
        X = X,
        Y = Y,
        Size = Size
    };
}

/// <summary>Um perfil do usuário: um nome e a disposição dos botões/joystick (o que cada um envia).</summary>
public sealed class Profile
{
    public string Name { get; set; } = "Perfil";
    public List<ButtonLayout> Layout { get; set; } = new();

    /// <summary>
    /// Camada de mouse na tela (toque = clique, duplo toque = clique direito, segurar = arrastar). Só vale se o servidor
    /// também ligar o mouse para o jogo. Perfis salvos antes desta opção não têm o campo e ficam com o padrão (ligado).
    /// </summary>
    public bool MouseEnabled { get; set; } = true;
}

// JSON gerado em tempo de compilação: funciona com trimming no build Release.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<GameInfo>))]
[JsonSerializable(typeof(List<ButtonLayout>))]
[JsonSerializable(typeof(List<Profile>))]
internal partial class ClientJson : JsonSerializerContext
{
}
