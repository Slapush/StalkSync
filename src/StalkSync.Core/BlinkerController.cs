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
    public bool AutoCancelEnabled { get; set; } = true;
    /// <summary>Fraction du braquage complet (0..1) à dépasser pour armer le retour automatique.</summary>
    public float AutoCancelSteer { get; set; } = 0.15f;
    public int CooldownMs { get; set; } = 350;
}

/// <summary>État du camion lu dans la télémétrie. Steer : positif = volant à gauche.</summary>
public readonly record struct GameState(
    bool Connected, bool Paused,
    bool LeftActive, bool RightActive,
    bool LeftLit, bool RightLit,
    bool Hazard, float Steer);

/// <summary>
/// Décide quelle touche envoyer au jeu pour que le clignotant suive le commodo.
/// Le jeu ne connaît que des bascules (un appui allume, un appui éteint) : on compare donc
/// l'état voulu à l'état réel lu en télémétrie, et on n'appuie que s'ils diffèrent.
/// </summary>
public sealed class BlinkerController
{
    private readonly BlinkerSettings _s;
    private Side _lastStalk = Side.Off;
    private long _pressStart;
    private long _lastTap = -1_000_000;
    private bool _comfort, _ignoreRelease, _confirmed, _pendingOff, _steerArmed, _lastLit;
    private int _blinks;

    public BlinkerController(BlinkerSettings settings) => _s = settings;

    /// <summary>État voulu, déduit du commodo.</summary>
    public Side Desired { get; private set; } = Side.Off;

    /// <summary>État supposé du jeu quand la télémétrie est absente.</summary>
    public Side Assumed { get; private set; } = Side.Off;

    public bool ComfortActive => _comfort;

    public BlinkerAction Update(long nowMs, bool leftPressed, bool rightPressed, GameState game, bool canSend)
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

        bool tel = game.Connected;
        if (tel && (game.Paused || game.Hazard || (game.LeftActive && game.RightActive)))
            return BlinkerAction.None;

        Side g = !tel ? Assumed : game.LeftActive ? Side.Left : game.RightActive ? Side.Right : Side.Off;
        if (tel) Assumed = g;

        if (Desired != Side.Off)
        {
            bool lit = tel && (Desired == Side.Left ? game.LeftLit : game.RightLit);
            if (lit && !_lastLit) _blinks++;
            _lastLit = lit;

            if (_comfort)
            {
                bool done = tel && _blinks >= _s.ComfortBlinks && !lit;
                long timeout = _s.ComfortBlinks * (tel ? 1200 : 700);
                if (done || nowMs - _pressStart > timeout) SetDesired(Side.Off);
            }
        }

        if (_s.Mode == StalkMode.Momentary && Desired != Side.Off)
        {
            if (_s.AutoCancelEnabled && tel && !_comfort)
            {
                float toward = Desired == Side.Left ? game.Steer : -game.Steer;
                if (toward > _s.AutoCancelSteer) _steerArmed = true;
                else if (_steerArmed && toward < _s.AutoCancelSteer * 0.3f) SetDesired(Side.Off);
            }
            // Le jeu a coupé le clignotant de lui-même : on suit au lieu de le rallumer.
            if (tel && _confirmed && g == Side.Off && Desired != Side.Off)
            {
                SetDesired(Side.Off);
                _pendingOff = false;
            }
        }

        if (Desired != Side.Off && g == Desired) _confirmed = true;
        if (Desired == Side.Off && g == Side.Off) _pendingOff = false;

        if (Desired == g) return BlinkerAction.None;
        // Commodo au centre : on n'éteint qu'une fois, pour ne pas lutter contre le clavier du joueur.
        if (Desired == Side.Off && !_pendingOff) return BlinkerAction.None;
        if (!canSend || nowMs - _lastTap < _s.CooldownMs) return BlinkerAction.None;

        var action = Desired == Side.Left ? BlinkerAction.TapLeft
                   : Desired == Side.Right ? BlinkerAction.TapRight
                   : g == Side.Left ? BlinkerAction.TapLeft : BlinkerAction.TapRight;
        _lastTap = nowMs;
        if (!tel)
        {
            var tapped = action == BlinkerAction.TapLeft ? Side.Left : Side.Right;
            Assumed = Assumed == tapped ? Side.Off : tapped;
        }
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
        _pendingOff = side == Side.Off;
        _comfort = _confirmed = _steerArmed = _lastLit = false;
        _blinks = 0;
    }
}
