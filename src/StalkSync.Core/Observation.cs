namespace StalkSync.Core;

/// <summary>
/// Valeur lue en télémétrie. Elle n'est retenue que si elle est stable et apparue après notre dernier appui :
/// une télémétrie lente renvoie encore l'ancien état juste après un appui, et ne doit pas faire croire à un écart.
/// </summary>
public sealed class Observation<T> where T : struct
{
    private T _value;
    private long _since;
    private bool _has;

    public void Feed(long nowMs, T value)
    {
        if (_has && EqualityComparer<T>.Default.Equals(value, _value)) return;
        _value = value;
        _since = nowMs;
        _has = true;
    }

    public void Clear() => _has = false;

    /// <param name="lastActionMs">Moment de notre dernier appui.</param>
    /// <param name="stableMs">Durée minimale pendant laquelle la valeur doit rester la même.</param>
    /// <param name="giveUpMs">Au-delà, l'appui n'a pas été pris en compte par le jeu : la valeur fait foi.</param>
    public bool TryGet(long nowMs, long lastActionMs, int stableMs, int giveUpMs, out T value)
    {
        value = _value;
        return _has && nowMs - _since >= stableMs && (_since > lastActionMs || nowMs - lastActionMs >= giveUpMs);
    }
}
