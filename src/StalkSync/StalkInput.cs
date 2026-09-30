using SharpDX;
using SharpDX.DirectInput;

namespace StalkSync;

/// <summary>Lit les boutons des périphériques de jeu via DirectInput, sans accès exclusif.</summary>
public sealed class StalkInput : IDisposable
{
    private readonly DirectInput _di = new();
    private readonly Dictionary<Guid, (Joystick Stick, string Name)> _sticks = new();
    private readonly IntPtr _hwnd;
    private long _nextScan;

    public StalkInput(IntPtr hwnd) => _hwnd = hwnd;

    public int DeviceCount => _sticks.Count;

    /// <summary>Boutons de chaque périphérique branché. Rescanne les périphériques toutes les 3 s.</summary>
    public Dictionary<Guid, (string Name, bool[] Buttons)> Poll(long nowMs)
    {
        if (nowMs >= _nextScan)
        {
            _nextScan = nowMs + 3000;
            Scan();
        }

        var result = new Dictionary<Guid, (string, bool[])>();
        List<Guid>? lost = null;
        foreach (var (id, (stick, name)) in _sticks)
        {
            try
            {
                stick.Poll();
                result[id] = (name, stick.GetCurrentState().Buttons);
            }
            catch (SharpDXException)
            {
                (lost ??= new()).Add(id);
            }
        }
        if (lost != null)
            foreach (var id in lost)
            {
                _sticks[id].Stick.Dispose();
                _sticks.Remove(id);
            }
        return result;
    }

    private void Scan()
    {
        try
        {
            foreach (var dev in _di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                if (_sticks.ContainsKey(dev.InstanceGuid)) continue;
                var stick = new Joystick(_di, dev.InstanceGuid);
                try
                {
                    stick.SetCooperativeLevel(_hwnd, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    stick.Acquire();
                    _sticks[dev.InstanceGuid] = (stick, dev.InstanceName.TrimEnd('\0'));
                }
                catch (SharpDXException)
                {
                    stick.Dispose();
                }
            }
        }
        catch (SharpDXException) { }
    }

    public void Dispose()
    {
        foreach (var (stick, _) in _sticks.Values) stick.Dispose();
        _sticks.Clear();
        _di.Dispose();
    }
}
