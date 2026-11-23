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
    public List<string> AllowedKeys { get; set; } = new();
    public List<ButtonLayout> Layout { get; set; } = new();
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
}

// JSON gerado em tempo de compilação: funciona com trimming no build Release.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<GameInfo>))]
[JsonSerializable(typeof(List<ButtonLayout>))]
[JsonSerializable(typeof(List<Profile>))]
internal partial class ClientJson : JsonSerializerContext
{
}
