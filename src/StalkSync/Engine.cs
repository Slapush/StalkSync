using System.Diagnostics;
using StalkSync.Core;

namespace StalkSync;

public sealed record EngineStatus(
    GameState Game, uint GameId, float SpeedKmh, bool GameFocused,
    bool LeftPressed, bool RightPressed, bool LeftFound, bool RightFound,
    Side Desired, bool Comfort, int Devices, string LastAction);

/// <summary>Boucle de fond : lit le commodo et la télémétrie, puis envoie les touches nécessaires.</summary>
public sealed class Engine : IDisposable
{
    private readonly Config _config;
    private readonly StalkInput _input;
    private readonly TelemetryReader _telemetry = new();
    private readonly Thread _thread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private volatile bool _running = true;
    private BlinkerController _controller;
    private string _lastAction = "—";

    private Action<ButtonBinding>? _detect;
    private Dictionary<Guid, bool[]>? _detectBaseline;
    private List<int>? _detectButtons;
    private Guid _detectDevice;
    private long _detectStart;

    public volatile EngineStatus? Status;

    public Engine(Config config, IntPtr hwnd)
    {
        _config = config;
        _input = new StalkInput(hwnd);
        _controller = new BlinkerController(config.Blinker);
        _thread = new Thread(Loop) { IsBackground = true, Name = "StalkSync" };
        _thread.Start();
    }

    /// <summary>Attend le prochain bouton enfoncé sur n'importe quel périphérique.</summary>
    public void BeginDetect(Action<ButtonBinding> onFound)
    {
        _detectBaseline = null;
        _detectButtons = null;
        _detect = onFound;
    }

    public void CancelDetect() => _detect = null;

    /// <summary>À appeler après un changement de réglages : repart d'un état propre.</summary>
    public void ResetController() => _controller = new BlinkerController(_config.Blinker);

    private void Loop()
    {
        while (_running)
        {
            try { Tick(); }
            catch (Exception e) { _lastAction = "Erreur : " + e.Message; }
            Thread.Sleep(15);
        }
    }

    private void Tick()
    {
        long now = _clock.ElapsedMilliseconds;
        var devices = _input.Poll(now);

        if (_detect is { } detect)
        {
            if (_detectBaseline == null)
                _detectBaseline = devices.ToDictionary(d => d.Key, d => d.Value.Buttons);
            else if (_detectButtons == null)
            {
                foreach (var (id, (_, buttons)) in devices)
                {
                    _detectBaseline.TryGetValue(id, out var before);
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (!buttons[i] || (before != null && before[i])) continue;
                        _detectDevice = id;
                        _detectButtons = [i];
                        _detectStart = now;
                        return;
                    }
                }
            }
            else
            {
                // Un commodo peut changer de bouton entre la mi-course et la position à fond :
                // on retient tous les boutons enfoncés jusqu'au relâchement ou pendant 1,5 s.
                bool held = false;
                if (devices.TryGetValue(_detectDevice, out var dev))
                {
                    _detectBaseline.TryGetValue(_detectDevice, out var before);
                    for (int i = 0; i < dev.Buttons.Length; i++)
                    {
                        if (!dev.Buttons[i]) continue;
                        if (_detectButtons.Contains(i)) held = true;
                        else if (before == null || !before[i]) { _detectButtons.Add(i); held = true; }
                    }
                }
                if (held && now - _detectStart < 1500) return;
                _detect = null;
                detect(new ButtonBinding
                {
                    Device = _detectDevice,
                    DeviceName = dev.Name ?? "",
                    Button = _detectButtons[0],
                    Extra = _detectButtons.Skip(1).ToArray(),
                });
            }
            return;
        }

        bool left = IsPressed(devices, _config.StalkLeft, out bool leftFound);
        bool right = IsPressed(devices, _config.StalkRight, out bool rightFound);
        var game = _telemetry.Read(now);
        bool focused = Native.GameHasFocus();

        var controller = _controller;
        var action = controller.Update(now, left, right, game, focused);
        if (action != BlinkerAction.None)
        {
            Native.TapKey(action == BlinkerAction.TapLeft ? _config.KeyLeft : _config.KeyRight);
            _lastAction = $"{DateTime.Now:HH:mm:ss} touche {(action == BlinkerAction.TapLeft ? "gauche" : "droite")}";
        }

        Status = new EngineStatus(game, _telemetry.Game, _telemetry.SpeedKmh, focused,
            left, right, leftFound, rightFound, controller.Desired, controller.ComfortActive,
            devices.Count, _lastAction);
    }

    private static bool IsPressed(Dictionary<Guid, (string Name, bool[] Buttons)> devices, ButtonBinding? b, out bool found)
    {
        found = false;
        if (b == null || !devices.TryGetValue(b.Device, out var d)) return false;
        found = true;
        return b.All.Any(i => i < d.Buttons.Length && d.Buttons[i]);
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(500);
        _input.Dispose();
        _telemetry.Dispose();
    }
}
