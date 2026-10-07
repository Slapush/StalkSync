namespace StalkSync.Core;

/// <summary>
/// Commodo qui n'envoie qu'une impulsion en arrivant sur chaque position : on retient la dernière
/// pour obtenir l'équivalent d'un commodo maintenu.
/// </summary>
public sealed class PulseStalk
{
    private bool _left, _right, _center;

    public Side Position { get; private set; } = Side.Off;

    public Side Update(bool left, bool right, bool center)
    {
        if (left && !_left) Position = Side.Left;
        else if (right && !_right) Position = Side.Right;
        else if (center && !_center) Position = Side.Off;
        (_left, _right, _center) = (left, right, center);
        return Position;
    }
}
