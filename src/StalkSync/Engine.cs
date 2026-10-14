using System.Diagnostics;
using StalkSync.Core;

namespace StalkSync;

/// <param name="Game">Jeu au premier plan, ou null.</param>
public sealed record EngineStatus(
    GameProfile? Game, Side Stalk, bool LeftFound, bool RightFound, bool CenterFound,
    Side Desired, Side Assumed, bool Comfort, int Devices, string LastAction,
    bool LightsEnabled, LightState Ring, BeamPosition Beam,
    LightState ShownCycle, bool ShownHigh, bool ShownFlash, bool ShownHighInCycle,
    bool TelemetryEnabled, BeamNgState? Telemetry, string? TelemetryError, int TelemetryPort);

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
    // Créé seulement quand la télémétrie est activée : sinon le port 4444 reste libre.
    private OutGauge? _outGauge;
    private readonly DebugMirror _debug = new();
    private Side _lastStalkSide;
    private LightState _lastRing;
    private BeamPosition _lastBeam;
    private string? _lastFocus;
    private readonly LightStalk _lightStalk = new();
    private readonly Dictionary<GameProfile, LightController> _lights = new();
    // Touche d'appel de phare enfoncée, par jeu, pour relâcher exactement celle-là.
    private readonly Dictionary<GameProfile, KeyBinding> _heldFlash = new();
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
            Thread.Sleep(5);
        }
        // Fermeture : ne jamais laisser une touche d'appel de phare enfoncée.
        foreach (var key in _heldFlash.Values) Native.SetKey(key, false);
        _heldFlash.Clear();
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
        foreach (var gone in _lights.Keys.Where(g => !games.Contains(g)).ToList())
        {
            if (_heldFlash.Remove(gone, out var held)) Native.SetKey(held, false);
            _lights.Remove(gone);
        }

        var lights = _config.Lights;
        if (lights.Enabled)
            _lightStalk.Update(
                IsPressed(devices, lights.Off, out _), IsPressed(devices, lights.Parking, out _),
                IsPressed(devices, lights.Low, out _), IsPressed(devices, lights.High, out _),
                IsPressed(devices, lights.Flash, out _), IsPressed(devices, lights.Neutral, out _));

        var process = Native.ForegroundProcess();
        GameProfile? focused = process == null ? null
            : games.FirstOrDefault(g => g.Enabled && g.Processes.Any(p => p.Equals(process, StringComparison.OrdinalIgnoreCase)));
        if (focused != null) _lastGame = focused;

        if (_config.BeamNgTelemetry && _outGauge == null)
            _outGauge = new OutGauge(_config.OutGaugePort, () => _clock.ElapsedMilliseconds, _debug.Packet);
        else if (!_config.BeamNgTelemetry && _outGauge != null)
        {
            _outGauge?.Dispose();
            _outGauge = null;
        }
        var beamNg = _outGauge?.Read(now);

        // Journal pour l'outil de debug : position du commodo et jeu au premier plan.
        var stalkSide = left == right ? Side.Off : left ? Side.Left : Side.Right;
        if (stalkSide != _lastStalkSide) _debug.Event(now, "commodo", new { position = stalkSide.ToString() });
        if (lights.Enabled && (_lightStalk.Ring != _lastRing || _lightStalk.Beam != _lastBeam))
            _debug.Event(now, "commodo-phares", new { bague = _lightStalk.Ring.ToString(), levier = _lightStalk.Beam.ToString() });
        if (focused?.Name != _lastFocus) _debug.Event(now, "premier-plan", new { jeu = focused?.Name, processus = process });
        (_lastStalkSide, _lastRing, _lastBeam, _lastFocus) = (stalkSide, _lightStalk.Ring, _lightStalk.Beam, focused?.Name);

        // Tous les jeux suivent le commodo, mais seul celui au premier plan reçoit des touches.
        foreach (var game in games)
        {
            var telemetry = Config.IsBeamNg(game) ? beamNg : null;
            // Télémétrie : on recale l'état supposé sur l'état réel ; warnings allumés → on ne touche à rien.
            if (telemetry is { Hazard: false } t)
            {
                var before = ControllerFor(game).Assumed;
                ControllerFor(game).Observe(now, t.Signal);
                if (ControllerFor(game).Assumed != before)
                    _debug.Event(now, "recalage-clignotant", new { jeu = game.Name, avant = before.ToString(), apres = t.Signal.ToString() });
            }
            bool canBlink = game == focused && game.Ready && telemetry is not { Hazard: true };
            var action = ControllerFor(game).Update(now, left, right, canBlink);
            if (action != BlinkerAction.None)
            {
                Native.TapKey(action == BlinkerAction.TapLeft ? game.KeyLeft! : game.KeyRight!);
                _debug.Event(now, "touche-clignotant", new
                {
                    jeu = game.Name, cote = action == BlinkerAction.TapLeft ? "gauche" : "droite",
                    voulu = ControllerFor(game).Desired.ToString(), suppose = ControllerFor(game).Assumed.ToString(),
                });
                _lastAction = $"{DateTime.Now:HH:mm:ss} {(action == BlinkerAction.TapLeft ? "gauche" : "droite")} → {game.Name}";
            }
            UpdateLights(game, now, lights.Enabled && game == focused && game.Enabled, telemetry);
        }

        var shownGame = _lastGame is { } last && games.Contains(last) ? last : games[0];
        var shown = ControllerFor(shownGame);
        var shownLights = _lights.GetValueOrDefault(shownGame) ?? new LightController();
        var stalk = left == right ? Side.Off : left ? Side.Left : Side.Right;
        Status = new EngineStatus(focused, stalk, leftFound, rightFound, centerFound,
            shown.Desired, shown.Assumed, shown.ComfortActive, devices.Count, _lastAction,
            lights.Enabled, _lightStalk.Ring, _lightStalk.Beam,
            shownLights.AssumedCycle, shownLights.AssumedHigh, shownLights.FlashDown, shownGame.HighInCycle,
            _config.BeamNgTelemetry, Config.IsBeamNg(shownGame) ? beamNg : null, _outGauge?.Error, _config.OutGaugePort);
    }

    private void UpdateLights(GameProfile game, long now, bool canSend, BeamNgState? telemetry)
    {
        if (!_lights.TryGetValue(game, out var c))
            _lights[game] = c = new LightController();
        if (telemetry is { } t)
        {
            var (cycleBefore, highBefore) = (c.AssumedCycle, c.AssumedHigh);
            c.Observe(now, game.LightCycle, t.High, t.Low);
            if (c.AssumedCycle != cycleBefore || c.AssumedHigh != highBefore)
                _debug.Event(now, "recalage-phares", new
                {
                    jeu = game.Name, avant = cycleBefore.ToString(), apres = c.AssumedCycle.ToString(),
                    pleinsPharesAvant = highBefore, pleinsPharesApres = c.AssumedHigh, telemetrie = $"0x{t.ShowLights:X4}",
                });
        }

        // Touche d'appel changée pendant un appel : on relâche l'ancienne.
        if (_heldFlash.TryGetValue(game, out var held) && held != game.KeyFlash)
        {
            Native.SetKey(held, false);
            _heldFlash.Remove(game);
            c.FlashWasReleased();
        }

        var ring = _config.Lights.SkipParking && _lightStalk.Ring == LightState.Parking ? LightState.Off : _lightStalk.Ring;
        var action = c.Update(now, ring, _lightStalk.Beam, game.LightKeys, canSend);
        if (action != LightAction.None)
            _debug.Event(now, "touche-phares", new
            {
                jeu = game.Name, action = action.ToString(), suppose = c.AssumedCycle.ToString(),
                pleinsPhares = c.AssumedHigh, bague = ring.ToString(), levier = _lightStalk.Beam.ToString(),
            });
        switch (action)
        {
            case LightAction.Cycle:
                Native.TapKey(game.KeyLightCycle!);
                _lastAction = $"{DateTime.Now:HH:mm:ss} feux → {game.Name}";
                break;
            case LightAction.HighToggle:
                Native.TapKey(game.KeyHighBeam!);
                _lastAction = $"{DateTime.Now:HH:mm:ss} pleins phares → {game.Name}";
                break;
            case LightAction.FlashPress:
                _heldFlash[game] = game.KeyFlash!;
                Native.SetKey(game.KeyFlash!, true);
                _lastAction = $"{DateTime.Now:HH:mm:ss} appel de phare → {game.Name}";
                break;
            case LightAction.FlashRelease:
                if (_heldFlash.Remove(game, out var key)) Native.SetKey(key, false);
                break;
        }
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
        _outGauge?.Dispose();
        _debug.Dispose();
    }
}
