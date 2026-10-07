using System.Diagnostics;
using StalkSync.Core;

namespace StalkSync;

/// <param name="Game">Jeu au premier plan, ou null.</param>
public sealed record EngineStatus(
    GameProfile? Game, Side Stalk, bool LeftFound, bool RightFound, bool CenterFound,
    Side Desired, Side Assumed, bool Comfort, int Devices, string LastAction);

/// <summary>Boucle de fond : lit le commodo, puis envoie les touches nécessaires.</summary>
public sealed class Engine : IDisposable
{
    private readonly Config _config;
    private readonly StalkInput _input;
    private readonly Thread _thread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private volatile bool _running = true;
    // Un contrôleur par jeu : chacun retient l'état du clignotant dans son jeu.
    private readonly Dictionary<GameProfile, BlinkerController> _controllers = new();
    private volatile bool _reset;
    private GameProfile? _lastGame;
    private readonly PulseStalk _pulse = new();
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
    public void ResetController() => _reset = true;

    private BlinkerController ControllerFor(GameProfile game)
    {
        if (!_controllers.TryGetValue(game, out var c))
            _controllers[game] = c = new BlinkerController(_config.Blinker);
        return c;
    }

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
        bool center = IsPressed(devices, _config.StalkCenter, out bool centerFound);
        if (_config.Blinker.Mode == StalkMode.Positions)
        {
            var position = _pulse.Update(left, right, center);
            left = position == Side.Left;
            right = position == Side.Right;
        }
        // L'interface remplace la liste au lieu de la modifier : on lit une référence stable.
        var games = _config.Games;
        if (_reset)
        {
            _reset = false;
            foreach (var (game, c) in _controllers.ToList())
                _controllers[game] = new BlinkerController(_config.Blinker, c.Assumed);
        }
        foreach (var gone in _controllers.Keys.Where(g => !games.Contains(g)).ToList())
            _controllers.Remove(gone);

        var process = Native.ForegroundProcess();
        GameProfile? focused = process == null ? null
            : games.FirstOrDefault(g => g.Enabled && g.Processes.Any(p => p.Equals(process, StringComparison.OrdinalIgnoreCase)));
        if (focused != null) _lastGame = focused;

        // Tous les jeux suivent le commodo, mais seul celui au premier plan reçoit des touches.
        foreach (var game in games)
        {
            var action = ControllerFor(game).Update(now, left, right, game == focused && game.Ready);
            if (action == BlinkerAction.None) continue;
            Native.TapKey(action == BlinkerAction.TapLeft ? game.KeyLeft! : game.KeyRight!);
            _lastAction = $"{DateTime.Now:HH:mm:ss} {(action == BlinkerAction.TapLeft ? "gauche" : "droite")} → {game.Name}";
        }

        var shown = ControllerFor(_lastGame is { } last && games.Contains(last) ? last : games[0]);
        var stalk = left == right ? Side.Off : left ? Side.Left : Side.Right;
        Status = new EngineStatus(focused, stalk, leftFound, rightFound, centerFound,
            shown.Desired, shown.Assumed, shown.ComfortActive, devices.Count, _lastAction);
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
    }
}
