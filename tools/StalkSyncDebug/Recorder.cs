using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StalkSync.Debugging;

record ButtonEvent(double t, string peripherique, int bouton, bool appuye);
record KeyEvent(double t, string touche, string scan, bool appuye, bool injecte, string? jeu);

/// <summary>
/// Enregistre tout ce qui aide à diagnostiquer StalkSync : boutons des périphériques, télémétrie BeamNG
/// (relayée par StalkSync sur 127.0.0.1:4446, ou lue directement sur 4444 s'il est fermé), décisions de
/// StalkSync, touches arrivées dans un jeu et jeu au premier plan. Le tout finit dans un rapport JSON.
/// </summary>
sealed class Recorder : IDisposable
{
    private const int MirrorPort = 4446, OutGaugePort = 4444, MaxItems = 50_000;
    private const int OffShow = 44, OffDash = 40, OgMinSize = 92;

    private static readonly string[] GameProcesses =
        ["eurotrucks2", "amtrucks", "BeamNG.drive.x64", "BeamNG.x64", "BeamNG.drive", "acs", "acs_x86", "AC2-Win64-Shipping"];

    private static readonly string[] DlNames =
    [
        "SHIFT (bit 0)", "FULLBEAM pleins phares (bit 1)", "HANDBRAKE (bit 2)", "PITSPEED (bit 3)", "TC (bit 4)",
        "SIGNAL_L clignotant gauche (bit 5)", "SIGNAL_R clignotant droit (bit 6)", "SIGNAL_ANY (bit 7)",
        "OILWARN (bit 8)", "BATTERY (bit 9)", "ABS (bit 10)", "SPARE croisement ? (bit 11)",
    ];

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DateTime _started = DateTime.Now;
    private readonly object _lock = new();
    private readonly StalkInput _input;
    private readonly Thread _pollThread, _mirrorThread, _directThread;
    private volatile bool _running = true;
    private volatile UdpClient? _mirror, _direct;
    private IntPtr _hook;
    private readonly Native.HookProc _hookProc;

    // Données enregistrées.
    private readonly List<ButtonEvent> _buttons = [];
    private readonly List<KeyEvent> _keys = [];
    private readonly List<object> _foreground = [], _markers = [], _ogChanges = [], _initial = [];
    private readonly List<JsonNode?> _stalkSyncEvents = [];
    private readonly Dictionary<Guid, (string Name, bool[] Buttons)> _devices = [];
    private readonly List<int> _ogPerSecond = [];
    private readonly Dictionary<int, int> _bitToggles = [];
    private readonly Dictionary<string, int> _ogSources = [];
    private int _ogThisSecond, _ogSecond, _ogTotal;
    private double _ogLastArrival = -1, _ogMaxGap;
    private uint _lastShow = uint.MaxValue, _lastDash = uint.MaxValue;
    private JsonObject? _firstPacket;
    private string? _lastForeground;
    private int _lastCheck = -1;
    private volatile bool _stalkSyncRunning = true;

    public Recorder(IntPtr hwnd)
    {
        _input = new StalkInput(hwnd);
        _hookProc = KeyboardHook;
        _hook = Native.SetWindowsHookEx(Native.WhKeyboardLl, _hookProc, Native.GetModuleHandle(null), 0);
        _pollThread = Start(Poll, "Poll");
        _mirrorThread = Start(() => Listen(MirrorPort, "relais StalkSync"), "Mirror");
        _directThread = Start(() => Listen(OutGaugePort, "BeamNG direct"), "Direct");
    }

    private static Thread Start(ThreadStart work, string name)
    {
        var t = new Thread(work) { IsBackground = true, Name = name };
        t.Start();
        return t;
    }

    private double Now => Math.Round(_clock.Elapsed.TotalMilliseconds, 1);
    public TimeSpan Elapsed => _clock.Elapsed;

    // Compteurs affichés pendant l'enregistrement.
    public int OgRate { get { lock (_lock) return _ogPerSecond.Count > 0 ? _ogPerSecond[^1] : _ogThisSecond; } }
    public string OgSource { get { lock (_lock) return _ogSources.Count == 0 ? "aucune" : string.Join(" + ", _ogSources.Keys); } }
    public int ButtonCount { get { lock (_lock) return _buttons.Count; } }
    public int InjectedKeyCount { get { lock (_lock) return _keys.Count(k => k.injecte && k.appuye); } }
    public int StalkSyncEventCount { get { lock (_lock) return _stalkSyncEvents.Count; } }
    public int DeviceCount { get { lock (_lock) return _devices.Count; } }

    public void AddMarker(string note)
    {
        lock (_lock) _markers.Add(new { t = Now, note });
    }

    private void Poll()
    {
        while (_running)
        {
            try
            {
                var devices = _input.Poll(Environment.TickCount64);
                double t = Now;
                lock (_lock)
                {
                    foreach (var (id, (name, buttons)) in devices)
                    {
                        if (!_devices.ContainsKey(id))
                            _initial.Add(new
                            {
                                t, peripherique = name,
                                boutonsAppuyes = Enumerable.Range(0, buttons.Length).Where(i => buttons[i]).Select(i => i + 1).ToArray(),
                            });
                        if (_devices.TryGetValue(id, out var before))
                            for (int i = 0; i < buttons.Length && i < before.Buttons.Length; i++)
                                if (buttons[i] != before.Buttons[i] && _buttons.Count < MaxItems)
                                    _buttons.Add(new ButtonEvent(t, name, i + 1, buttons[i]));
                        _devices[id] = (name, (bool[])buttons.Clone());
                    }
                    // StalkSync ouvert : on lui laisse le port 4444, la télémétrie arrive par le relais.
                    if ((int)t / 1000 != _lastCheck)
                    {
                        _lastCheck = (int)t / 1000;
                        _stalkSyncRunning = Process.GetProcessesByName("StalkSync").Length > 0;
                        if (_stalkSyncRunning) _direct?.Dispose();
                    }
                    var fg = Native.ForegroundProcess();
                    if (fg != _lastForeground)
                    {
                        _foreground.Add(new { t, processus = fg });
                        _lastForeground = fg;
                    }
                    // Paquets par seconde.
                    int second = (int)(t / 1000);
                    while (_ogSecond < second)
                    {
                        _ogPerSecond.Add(_ogThisSecond);
                        _ogThisSecond = 0;
                        _ogSecond++;
                    }
                }
            }
            catch (Exception) { }
            Thread.Sleep(2);
        }
    }

    private void Listen(int port, string source)
    {
        bool direct = port == OutGaugePort;
        while (_running)
        {
            try
            {
                var socket = direct ? _direct : _mirror;
                if (socket == null)
                {
                    if (direct && _stalkSyncRunning)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)) { Client = { ReceiveBufferSize = 1 << 20 } };
                    if (direct) _direct = socket; else _mirror = socket;
                }
                IPEndPoint? from = null;
                var data = socket.Receive(ref from);
                if (port == MirrorPort && data.Length > 2 && data[0] == 'E' && data[1] == 'V')
                    StalkSyncEvent(data);
                else if (port == MirrorPort && data.Length > 2 && data[0] == 'O' && data[1] == 'G')
                    OutGaugePacket(data.AsSpan(2).ToArray(), source);
                else if (port == OutGaugePort)
                    OutGaugePacket(data, source);
            }
            catch (Exception e) when (_running && e is SocketException or ObjectDisposedException)
            {
                // Port pris (par StalkSync pour 4444) ou socket libérée : on réessaie plus tard.
                if (direct) { _direct?.Dispose(); _direct = null; } else { _mirror?.Dispose(); _mirror = null; }
                Thread.Sleep(1000);
            }
            catch (Exception) when (!_running) { return; }
        }
    }

    private void StalkSyncEvent(byte[] data)
    {
        try
        {
            var node = JsonNode.Parse(Encoding.UTF8.GetString(data, 2, data.Length - 2));
            if (node is JsonObject o)
            {
                o["tStalkSync"] = o["t"]?.DeepClone();
                o["t"] = Now;
            }
            lock (_lock) if (_stalkSyncEvents.Count < MaxItems) _stalkSyncEvents.Add(node);
        }
        catch (JsonException) { }
    }

    private void OutGaugePacket(byte[] p, string source)
    {
        if (p.Length < OgMinSize) return;
        double t = Now;
        uint dash = BitConverter.ToUInt32(p, OffDash), show = BitConverter.ToUInt32(p, OffShow);
        lock (_lock)
        {
            _ogTotal++;
            _ogThisSecond++;
            _ogSources[source] = _ogSources.GetValueOrDefault(source) + 1;
            if (_ogLastArrival >= 0) _ogMaxGap = Math.Max(_ogMaxGap, t - _ogLastArrival);
            _ogLastArrival = t;
            _firstPacket ??= Decode(p);
            if (show == _lastShow && dash == _lastDash) return;
            uint changed = _lastShow == uint.MaxValue ? 0 : show ^ _lastShow;
            for (int b = 0; b < 32; b++)
                if ((changed & (1u << b)) != 0) _bitToggles[b] = _bitToggles.GetValueOrDefault(b) + 1;
            if (_ogChanges.Count < MaxItems)
                _ogChanges.Add(new
                {
                    t, source,
                    voyants = $"0x{show:X4}", disponibles = $"0x{dash:X4}",
                    allumes = Bits(show), changes = Bits(changed),
                });
            (_lastShow, _lastDash) = (show, dash);
        }
    }

    private static string[] Bits(uint value) =>
        Enumerable.Range(0, 32).Where(b => (value & (1u << b)) != 0)
            .Select(b => b < DlNames.Length ? DlNames[b] : $"bit {b}").ToArray();

    private static JsonObject Decode(byte[] p) => new()
    {
        ["taille"] = p.Length,
        ["voiture"] = Encoding.ASCII.GetString(p, 4, 4).TrimEnd('\0'),
        ["flags"] = BitConverter.ToUInt16(p, 8),
        ["rapport"] = (int)p[10],
        ["vitesseKmh"] = Math.Round(BitConverter.ToSingle(p, 12) * 3.6, 1),
        ["regime"] = Math.Round(BitConverter.ToSingle(p, 16)),
        ["voyantsDisponibles"] = $"0x{BitConverter.ToUInt32(p, OffDash):X4}",
        ["voyantsAllumes"] = $"0x{BitConverter.ToUInt32(p, OffShow):X4}",
    };

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KbdLlHookStruct>(lParam);
            int msg = (int)wParam;
            bool down = msg is Native.WmKeyDown or Native.WmSysKeyDown;
            bool injected = (k.Flags & Native.LlkhfInjected) != 0;
            var fg = Native.ForegroundProcess();
            bool inGame = fg != null && GameProcesses.Any(g => g.Equals(fg, StringComparison.OrdinalIgnoreCase));
            // Vie privée : seules les touches envoyées dans un jeu, ou envoyées par StalkSync, sont gardées.
            if (inGame || injected)
                lock (_lock)
                    if (_keys.Count < MaxItems)
                        _keys.Add(new KeyEvent(Now, ((Keys)k.VkCode).ToString(), $"0x{k.ScanCode:X2}", down, injected, fg));
        }
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Arrête l'enregistrement et écrit le rapport ; renvoie son chemin.</summary>
    public string Save(string toolVersion)
    {
        Stop();
        JsonNode? config = null;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StalkSync", "config.json");
            if (File.Exists(path)) config = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        object report;
        lock (_lock)
        {
            if (_ogThisSecond > 0) _ogPerSecond.Add(_ogThisSecond);
            var rates = _ogPerSecond.Where(r => r > 0).ToList();
            var injected = _keys.Where(k => k.injecte && k.appuye).GroupBy(k => k.touche).ToDictionary(g => g.Key, g => g.Count());
            report = new
            {
                outil = "StalkSyncDebug " + toolVersion,
                date = _started.ToString("yyyy-MM-dd HH:mm:ss"),
                dureeSecondes = Math.Round(_clock.Elapsed.TotalSeconds, 1),
                windows = Environment.OSVersion.VersionString,
                stalkSyncOuvert = Process.GetProcessesByName("StalkSync").Length > 0,
                resume = new
                {
                    telemetrie = new
                    {
                        source = _ogSources,
                        paquets = _ogTotal,
                        paquetsParSecondeMoyen = rates.Count > 0 ? Math.Round(rates.Average()) : 0,
                        paquetsParSecondeMin = rates.Count > 0 ? rates.Min() : 0,
                        paquetsParSecondeMax = rates.Count > 0 ? rates.Max() : 0,
                        plusLongTrouMs = Math.Round(_ogMaxGap, 1),
                        basculesParVoyant = _bitToggles.OrderBy(b => b.Key)
                            .ToDictionary(b => b.Key < DlNames.Length ? DlNames[b.Key] : $"bit {b.Key}", b => b.Value),
                    },
                    appuisBoutons = _buttons.Count(b => b.appuye),
                    touchesEnvoyeesParStalkSync = injected,
                    evenementsStalkSync = _stalkSyncEvents.Count,
                    reperes = _markers.Count,
                },
                peripheriques = _devices.Select(d => new { nom = d.Value.Name, id = d.Key, boutons = d.Value.Buttons.Length }),
                configStalkSync = config,
                reperes = _markers,
                etatInitialBoutons = _initial,
                evenementsStalkSync = _stalkSyncEvents,
                boutons = _buttons,
                touches = _keys,
                premierPlan = _foreground,
                telemetrie = new { premierPaquet = _firstPacket, paquetsParSeconde = _ogPerSecond, changements = _ogChanges },
            };
        }

        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"StalkSync-debug-{_started:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        return file;
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _mirror?.Dispose();
        _direct?.Dispose();
        _pollThread.Join(500);
        _mirrorThread.Join(500);
        _directThread.Join(500);
    }

    public void Dispose()
    {
        Stop();
        _input.Dispose();
    }
}
