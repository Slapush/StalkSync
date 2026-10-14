using System.Text.Json;
using System.Text.Json.Serialization;
using StalkSync.Core;

namespace StalkSync;

public sealed class ButtonBinding
{
    public Guid Device { get; set; }
    public string DeviceName { get; set; } = "";
    public int Button { get; set; }
    /// <summary>Autres boutons du même côté (ex. position à fond distincte de la mi-course).</summary>
    public int[] Extra { get; set; } = [];
    [JsonIgnore] public IEnumerable<int> All => Extra.Prepend(Button);
    public override string ToString() => $"{DeviceName} — bouton {string.Join(" + ", All.Select(b => b + 1))}";
}

public sealed class KeyBinding
{
    public ushort ScanCode { get; set; }
    public bool Extended { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Boutons du commodo de phares (une impulsion par position, comme le commodo Moza).</summary>
public sealed class LightBindings
{
    public bool Enabled { get; set; }
    public ButtonBinding? Off { get; set; }
    public ButtonBinding? Parking { get; set; }
    public ButtonBinding? Low { get; set; }
    public ButtonBinding? High { get; set; }
    public ButtonBinding? Flash { get; set; }
    public ButtonBinding? Neutral { get; set; }
}

/// <summary>Un jeu reconnu par le nom de son processus, avec ses touches de clignotant et de feux.</summary>
public sealed class GameProfile
{
    public string Name { get; set; } = "";
    /// <summary>Noms de processus, sans « .exe ».</summary>
    public string[] Processes { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public bool Builtin { get; set; }
    public KeyBinding? KeyLeft { get; set; }
    public KeyBinding? KeyRight { get; set; }
    /// <summary>Touche « mode des feux » et ordre des états qu'elle parcourt dans ce jeu.</summary>
    public KeyBinding? KeyLightCycle { get; set; }
    public LightState[] LightCycle { get; set; } = [];
    /// <summary>Touche dédiée aux pleins phares (inutile si la touche de mode passe déjà par eux).</summary>
    public KeyBinding? KeyHighBeam { get; set; }
    /// <summary>Touche d'appel de phare, maintenue tant que le commodo est tiré.</summary>
    public KeyBinding? KeyFlash { get; set; }

    [JsonIgnore] public bool Ready => Enabled && KeyLeft != null && KeyRight != null;
    [JsonIgnore] public bool HighInCycle => LightCycle.Contains(LightState.High);
    [JsonIgnore] public LightKeys LightKeys =>
        new(LightCycle, KeyLightCycle != null, KeyHighBeam != null && !HighInCycle, KeyFlash != null);
}

public sealed class Config
{
    public ButtonBinding? StalkLeft { get; set; }
    public ButtonBinding? StalkRight { get; set; }
    /// <summary>Bouton de la position centrale, utilisé seulement par le type <see cref="StalkMode.Positions"/>.</summary>
    public ButtonBinding? StalkCenter { get; set; }
    public LightBindings Lights { get; set; } = new();
    public List<GameProfile> Games { get; set; } = [];
    public BlinkerSettings Blinker { get; set; } = new();
    public bool StartMinimized { get; set; }

    // Anciennes versions : touches uniques pour ETS2 / ATS, reprises dans leur profil au chargement.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KeyBinding? KeyLeft { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KeyBinding? KeyRight { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StalkSync", "config.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Config Load()
    {
        Config? config = null;
        try
        {
            if (File.Exists(FilePath))
                config = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        config ??= new Config();
        config.AddMissingGames();
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    private void AddMissingGames()
    {
        foreach (var game in BuiltinGames())
        {
            var existing = Games.Find(g => g.Builtin && g.Name == game.Name);
            if (existing == null)
                Games.Add(game);
            else
            {
                existing.Processes = game.Processes;
                // Réglages d'avant la gestion des phares : on reprend les touches de feux par défaut.
                if (existing.LightCycle.Length == 0)
                    (existing.LightCycle, existing.KeyLightCycle, existing.KeyHighBeam, existing.KeyFlash) =
                        (game.LightCycle, game.KeyLightCycle, game.KeyHighBeam, game.KeyFlash);
            }
        }
        foreach (var game in Games.Where(g => g.LightCycle.Length == 0))
            game.LightCycle = [LightState.Off, LightState.Low];
        if (KeyLeft != null && KeyRight != null)
        {
            var trucks = Games.First(g => g.Name == TrucksName);
            (trucks.KeyLeft, trucks.KeyRight) = (KeyLeft, KeyRight);
        }
        KeyLeft = KeyRight = null;
    }

    private const string TrucksName = "Euro Truck Simulator 2 / American Truck Simulator";

    /// <summary>Nom court pour l'en-tête.</summary>
    public static string ShortName(GameProfile game) => game.Name switch
    {
        TrucksName => "ETS2 / ATS",
        "Assetto Corsa Competizione" => "ACC",
        _ => game.Name,
    };

    /// <summary>Notes affichées sous les touches de chaque jeu intégré.</summary>
    public static string Hint(GameProfile game) => game.Name switch
    {
        TrucksName => "Mêmes touches que dans Options → Touches (phares : L, K et J). Retire le commodo des "
                    + "clignotants et des phares du jeu, et désactive l'extinction automatique des clignotants.",
        "BeamNG.drive" => "Mêmes touches que dans Options → Contrôles (par défaut , et . ; phares N). "
                        + "L'appel de phare n'a pas de touche par défaut : assigne K à « Flash high beam ». "
                        + "Retire le commodo des clignotants et des phares du jeu.",
        "Assetto Corsa" => "Les clignotants viennent de Custom Shaders Patch : assigne-leur une touche "
                         + "dans les commandes (Content Manager), puis la même ici.",
        "Assetto Corsa Competizione" => "Mêmes touches que dans Options → Commandes (par défaut Alt + ← et Alt + →).",
        _ => "Indique les touches de clignotant réglées dans le jeu, et retire le commodo des clignotants du jeu.",
    };

    private static IEnumerable<GameProfile> BuiltinGames()
    {
        // ETS2 / ATS : [ et ] par position physique (clavier US), comme le jeu.
        yield return new GameProfile
        {
            Name = TrucksName, Builtin = true, Processes = ["eurotrucks2", "amtrucks"],
            KeyLeft = new() { ScanCode = 0x1A, Name = "[" },
            KeyRight = new() { ScanCode = 0x1B, Name = "]" },
            // L : éteint → veilleuses → croisement ; K : pleins phares ; J : appel de phare.
            KeyLightCycle = new() { ScanCode = 0x26, Name = "L" },
            LightCycle = [LightState.Off, LightState.Parking, LightState.Low],
            KeyHighBeam = new() { ScanCode = 0x25, Name = "K" },
            KeyFlash = new() { ScanCode = 0x24, Name = "J" },
        };
        // BeamNG : virgule et point selon la disposition du clavier.
        yield return new GameProfile
        {
            Name = "BeamNG.drive", Builtin = true, Processes = ["BeamNG.drive.x64", "BeamNG.x64", "BeamNG.drive"],
            KeyLeft = FromVirtualKey(0xBC, ","),
            KeyRight = FromVirtualKey(0xBE, "."),
            // N : éteint → croisement → pleins phares ; pas de veilleuses. K, libre par défaut, pour l'appel.
            KeyLightCycle = FromVirtualKey(0x4E, "N"),
            LightCycle = [LightState.Off, LightState.Low, LightState.High],
            KeyFlash = new() { ScanCode = 0x25, Name = "K" },
        };
        yield return new GameProfile
        {
            Name = "Assetto Corsa", Builtin = true, Processes = ["acs", "acs_x86"],
        };
        yield return new GameProfile
        {
            Name = "Assetto Corsa Competizione", Builtin = true, Processes = ["AC2-Win64-Shipping"],
            KeyLeft = new() { ScanCode = 0x4B, Extended = true, Alt = true, Name = "Left" },
            KeyRight = new() { ScanCode = 0x4D, Extended = true, Alt = true, Name = "Right" },
        };
    }

    private static KeyBinding FromVirtualKey(uint vk, string name) =>
        new() { ScanCode = (ushort)Native.MapVirtualKey(vk, 0), Name = name };
}
