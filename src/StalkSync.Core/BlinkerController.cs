namespace StalkSync.Core;

public enum Side { Off, Left, Right }

public enum StalkMode
{
    /// <summary>Le commodo reste en position et maintient son bouton appuyé.</summary>
    Latching,
    /// <summary>Le commodo revient au centre et n'envoie qu'une impulsion.</summary>
    Momentary,
    /// <summary>
    /// Un bouton par position (gauche, centre, droite), chacun n'envoyant qu'une impulsion à l'arrivée
    /// sur sa position (ex. Moza). Se comporte comme <see cref="Latching"/> une fois traduit par <see cref="PulseStalk"/>.
    /// </summary>
    Positions,
}

public enum BlinkerAction { None, TapLeft, TapRight }

public sealed class BlinkerSettings
{
    public StalkMode Mode { get; set; } = StalkMode.Latching;
    public bool ComfortEnabled { get; set; } = true;
    public int ComfortBlinks { get; set; } = 3;
    public int ComfortMaxPressMs { get; set; } = 400;
    public int CooldownMs { get; set; } = 350;
}

/// <summary>
/// Décide quelle touche envoyer au jeu pour que le clignotant suive le commodo.
/// Le jeu ne connaît que des bascules (un appui allume, un appui éteint) : on garde donc en mémoire
/// l'état supposé du jeu, et on n'appuie que quand il diffère de la position du commodo.
/// </summary>
public sealed class BlinkerController
{
    /// <summary>Durée approximative d'un clignotement dans ETS2 / ATS.</summary>
    public const int BlinkPeriodMs = 700;

    /// <summary>Durée pendant laquelle l'état lu en télémétrie doit rester stable avant d'être retenu.</summary>
    public const int SyncStableMs = 600;
    /// <summary>Au-delà, un état contraire à notre dernier appui veut dire que le jeu ne l'a pas pris en compte.</summary>
    public const int SyncGiveUpMs = 3000;

    private readonly Observation<Side> _observed = new();

    private readonly BlinkerSettings _s;
    private Side _lastStalk = Side.Off;
    private long _pressStart;
    private long _lastTap = -1_000_000;
    private bool _comfort, _ignoreRelease;

    public BlinkerController(BlinkerSettings settings, Side assumed = Side.Off)
    {
        _s = settings;
        Assumed = assumed;
    }

    /// <summary>État voulu, déduit du commodo.</summary>
    public Side Desired { get; private set; } = Side.Off;

    /// <summary>État supposé du clignotant dans le jeu.</summary>
    public Side Assumed { get; private set; }

    public bool ComfortActive => _comfort;

    /// <summary>Recale l'état supposé sur l'état réel lu en télémétrie.</summary>
    public void Observe(long nowMs, Side game)
    {
        _observed.Feed(nowMs, game);
        if (_observed.TryGet(nowMs, _lastTap, SyncStableMs, SyncGiveUpMs, out var side)) Assumed = side;
    }

    public BlinkerAction Update(long nowMs, bool leftPressed, bool rightPressed, bool canSend)
    {
        var stalk = leftPressed && rightPressed ? _lastStalk
                  : leftPressed ? Side.Left
                  : rightPressed ? Side.Right
                  : Side.Off;
        if (stalk != _lastStalk)
        {
            OnStalkChange(nowMs, _lastStalk, stalk);
            _lastStalk = stalk;
        }

        if (_comfort && nowMs - _pressStart > _s.ComfortBlinks * BlinkPeriodMs)
            SetDesired(Side.Off);

        if (Desired == Assumed) return BlinkerAction.None;
        if (!canSend || nowMs - _lastTap < _s.CooldownMs) return BlinkerAction.None;

        var action = Desired == Side.Left ? BlinkerAction.TapLeft
                   : Desired == Side.Right ? BlinkerAction.TapRight
                   : Assumed == Side.Left ? BlinkerAction.TapLeft : BlinkerAction.TapRight;
        _lastTap = nowMs;
        var tapped = action == BlinkerAction.TapLeft ? Side.Left : Side.Right;
        Assumed = Assumed == tapped ? Side.Off : tapped;
        return action;
    }

    private void OnStalkChange(long now, Side previous, Side current)
    {
        if (_s.Mode != StalkMode.Momentary)
        {
            if (current != Side.Off)
            {
                SetDesired(current);
                _comfort = false;
                _pressStart = now;
            }
            else if (_s.ComfortEnabled && Desired == previous && now - _pressStart < _s.ComfortMaxPressMs)
                _comfort = true;
            else
                SetDesired(Side.Off);
            return;
        }

        if (current != Side.Off)
        {
            if (Desired == current)
            {
                SetDesired(Side.Off);
                _ignoreRelease = true;
            }
            else
            {
                SetDesired(current);
                _pressStart = now;
                _ignoreRelease = false;
            }
        }
        else if (!_ignoreRelease && Desired == previous
                 && _s.ComfortEnabled && now - _pressStart < _s.ComfortMaxPressMs)
            _comfort = true;
    }

    private void SetDesired(Side side)
    {
        if (side == Desired) return;
        Desired = side;
        _comfort = false;
    }
}
