using System.Text.Json;
using Android.Content;
using GameStream.Client.Models;

namespace GameStream.Client.Data;

/// <summary>Guarda os perfis do usuário no aparelho (até <see cref="MaxProfiles"/>).</summary>
public static class ProfileStore
{
    public const int MaxProfiles = 5;
    public const int MaxControls = 24;
    private const string PrefsKey = "profiles";

    public static List<Profile> Load(ISharedPreferences prefs) => Parse(prefs.GetString(PrefsKey, null));

    public static void Save(ISharedPreferences prefs, List<Profile> profiles) =>
        prefs.Edit()!.PutString(PrefsKey, ToJson(profiles))!.Apply();

    public static string ToJson(List<Profile> profiles) =>
        JsonSerializer.Serialize(profiles, ClientJson.Default.ListProfile);

    /// <summary>Lê o JSON salvo; se estiver vazio ou corrompido, devolve lista vazia (o app cria o Perfil 1).</summary>
    public static List<Profile> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<Profile>();
        try
        {
            var list = (JsonSerializer.Deserialize(json, ClientJson.Default.ListProfile) ?? new List<Profile>())
                .Where(p => p is not null)
                .Take(MaxProfiles)
                .ToList();

            for (int i = 0; i < list.Count; i++)
            {
                list[i].Layout ??= new List<ButtonLayout>();
                if (string.IsNullOrWhiteSpace(list[i].Name)) list[i].Name = $"Perfil {i + 1}";
            }
            return list;
        }
        catch (JsonException)
        {
            return new List<Profile>();
        }
    }

    /// <summary>Layout usado quando o jogo não traz um: joystick WASD e quatro botões.</summary>
    public static List<ButtonLayout> DefaultLayout() => new()
    {
        new() { Type = "dpad", Keys = new List<string> { "W", "A", "S", "D" }, X = 0.14, Y = 0.68, Size = 0.30 },
        new() { Type = "button", Key = "Z", X = 0.90, Y = 0.28 },
        new() { Type = "button", Key = "X", X = 0.90, Y = 0.44 },
        new() { Type = "button", Key = "C", X = 0.90, Y = 0.60 },
        new() { Type = "button", Key = "V", X = 0.90, Y = 0.76 }
    };
}
