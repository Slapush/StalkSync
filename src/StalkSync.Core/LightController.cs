namespace StalkSync.Core;

/// <summary>État des feux. L'ordre sert aux jeux qui passent d'un état à l'autre avec une seule touche.</summary>
public enum LightState { Off, Parking, Low, High }

/// <summary>Position du commodo sur l'axe avant / arrière.</summary>
public enum BeamPosition { Neutral, High, Flash }

public enum LightAction { None, Cycle, HighToggle, FlashPress, FlashRelease }

/// <summary>
/// Commodo de phares qui n'envoie qu'une impulsion en arrivant sur chaque position (ex. Moza) :
/// bague éteint / veilleuses / croisement, et levier pleins phares / appel / retour au neutre.
/// On retient la dernière impulsion de chaque groupe.
/// </summary>
public sealed class LightStalk
{
    private readonly bool[] _last = new bool[6];

    public LightState Ring { get; private set; } = LightState.Off;
    public BeamPosition Beam { get; private set; } = BeamPosition.Neutral;

    /// <param name="off">Bague sur « éteint ».</param>
    /// <param name="parking">Bague sur « veilleuses ».</param>
    /// <param name="low">Bague sur « croisement ».</param>
    /// <param name="high">Levier poussé : pleins phares.</param>
    /// <param name="flash">Levier tiré : appel de phare.</param>
    /// <param name="neutral">Levier revenu au neutre.</param>
    public void Update(bool off, bool parking, bool low, bool high, bool flash, bool neutral)
    {
        bool[] now = [off, parking, low, high, flash, neutral];
        bool Rising(int i) => now[i] && !_last[i];
        if (Rising(0)) Ring = LightState.Off;
        else if (Rising(1)) Ring = LightState.Parking;
        else if (Rising(2)) Ring = LightState.Low;
        if (Rising(3)) Beam = BeamPosition.High;
        else if (Rising(4)) Beam = BeamPosition.Flash;
        else if (Rising(5)) Beam = BeamPosition.Neutral;
        Array.Copy(now, _last, now.Length);
    }
}

/// <summary>Touches de feux disponibles dans un jeu, et ordre des états de sa touche « mode des feux ».</summary>
public sealed record LightKeys(IReadOnlyList<LightState> Cycle, bool HasCycle, bool HasHighToggle, bool HasFlash);

/// <summary>
/// Décide quelle touche de feux envoyer au jeu pour qu'il suive le commodo. Comme pour les clignotants,
/// le jeu ne connaît que des bascules : on retient l'état supposé du jeu et on n'appuie que s'il diffère.
/// </summary>
public sealed class LightController
{
    private readonly int _cooldownMs;
    private long _lastTap = -1_000_000, _lastFlash = -1_000_000;

    /// <summary>Délai pendant lequel la télémétrie est ignorée après un appui (le jeu met un moment à suivre).</summary>
    public const int SyncGraceMs = 800;

    public LightController(int cooldownMs = 250, LightState assumedCycle = LightState.Off, bool assumedHigh = false)
    {
        _cooldownMs = cooldownMs;
        AssumedCycle = assumedCycle;
        AssumedHigh = assumedHigh;
    }

    /// <summary>État supposé de la touche « mode des feux » dans le jeu.</summary>
    public LightState AssumedCycle { get; private set; }
    /// <summary>État supposé des pleins phares, pour les jeux qui ont une touche dédiée.</summary>
    public bool AssumedHigh { get; private set; }
    /// <summary>Vrai tant que la touche d'appel de phare est enfoncée dans le jeu.</summary>
    public bool FlashDown { get; private set; }

    public LightAction Update(long nowMs, LightState ring, BeamPosition beam, LightKeys keys, bool canSend)
    {
        // L'appel de phare passe en premier et ne dépend pas du délai entre deux appuis.
        bool flash = keys.HasFlash && canSend && beam == BeamPosition.Flash;
        if (flash != FlashDown)
        {
            FlashDown = flash;
            _lastFlash = nowMs;
            return flash ? LightAction.FlashPress : LightAction.FlashRelease;
        }
        if (!canSend || nowMs - _lastTap < _cooldownMs) return LightAction.None;

        var cycle = keys.Cycle;
        bool highInCycle = cycle.Contains(LightState.High);
        if (keys.HasCycle && cycle.Count > 0)
        {
            var wanted = ring;
            if (!cycle.Contains(wanted)) wanted = wanted == LightState.Parking ? LightState.Off : cycle[^1];
            if (highInCycle && wanted == LightState.Low && beam == BeamPosition.High) wanted = LightState.High;
            if (!cycle.Contains(AssumedCycle)) AssumedCycle = cycle[0];
            if (wanted != AssumedCycle)
            {
                int i = 0;
                while (cycle[i] != AssumedCycle) i++;
                AssumedCycle = cycle[(i + 1) % cycle.Count];
                _lastTap = nowMs;
                return LightAction.Cycle;
            }
        }

        if (keys.HasHighToggle && !highInCycle)
        {
            bool wantedHigh = beam == BeamPosition.High;
            if (wantedHigh != AssumedHigh)
            {
                AssumedHigh = wantedHigh;
                _lastTap = nowMs;
                return LightAction.HighToggle;
            }
        }
        return LightAction.None;
    }

    /// <summary>
    /// Recale l'état supposé sur l'état réel des pleins phares lu en télémétrie
    /// (et des feux de croisement si le jeu les fournit).
    /// </summary>
    public void Observe(long nowMs, IReadOnlyList<LightState> cycle, bool high, bool? low)
    {
        if (FlashDown || nowMs - _lastTap < SyncGraceMs || nowMs - _lastFlash < SyncGraceMs) return;
        if (cycle.Contains(LightState.High))
        {
            if (high) AssumedCycle = LightState.High;
            else if (low == true) AssumedCycle = LightState.Low;
            else if (low == false && AssumedCycle is LightState.Low or LightState.High)
                AssumedCycle = LightState.Off;
            // Sans info sur le croisement : on se croyait en pleins phares à tort, le jeu est resté un cran avant.
            else if (low == null && AssumedCycle == LightState.High) AssumedCycle = LightState.Low;
        }
        else
            AssumedHigh = high;
    }

    /// <summary>La touche d'appel a été relâchée de l'extérieur (changement de touche, fermeture).</summary>
    public void FlashWasReleased() => FlashDown = false;
}
