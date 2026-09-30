using System.Text.Json;
using StalkSync.Core;

namespace StalkSync;

public sealed class ButtonBinding
{
    public Guid Device { get; set; }
    public string DeviceName { get; set; } = "";
    public int Button { get; set; }
    public override string ToString() => $"{DeviceName} — bouton {Button + 1}";
}

public sealed class KeyBinding
{
    public ushort ScanCode { get; set; }
    public bool Extended { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Config
{
    public ButtonBinding? StalkLeft { get; set; }
    public ButtonBinding? StalkRight { get; set; }
    // Touches par défaut d'ETS2/ATS pour les clignotants : [ et ] (position physique, clavier US).
    public KeyBinding KeyLeft { get; set; } = new() { ScanCode = 0x1A, Name = "[" };
    public KeyBinding KeyRight { get; set; } = new() { ScanCode = 0x1B, Name = "]" };
    public BlinkerSettings Blinker { get; set; } = new();
    public bool StartMinimized { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StalkSync", "config.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
