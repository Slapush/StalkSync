using System.Reflection;
using StalkSync.Core;

namespace StalkSync;

public sealed class MainForm : Form
{
    private static readonly HashSet<Keys> ExtendedKeys = new()
    {
        Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Insert, Keys.Delete, Keys.Home, Keys.End,
        Keys.PageUp, Keys.PageDown, Keys.Divide, Keys.NumLock, Keys.RControlKey, Keys.RMenu,
    };

    private static readonly int[] ComfortDurations = Enumerable.Range(2, 29).Select(i => i * 50).ToArray();

    private readonly Config _config = Config.Load();
    private Engine? _engine;
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 50 };
    private readonly NotifyIcon _tray = new();
    private bool _loading = true, _quitting;
    private Action<KeyBinding>? _keyCapture;
    private Label? _detecting;

    private readonly Pill _pill = new();
    private readonly Label _lblDevice;
    private readonly ToolTip _tips = new();
    private readonly Lamp _lampLeft, _lampRight;
    private readonly Label _lblState, _lblStalk, _lblAction, _lblWarning;
    private readonly Label _lblStalkLeft, _lblStalkCenter, _lblStalkRight, _lblKeyLeft, _lblKeyRight, _lblGameHint;
    private readonly ComboBox _cmbGame;
    private readonly Toggle _tglGameEnabled = new("Activé pour ce jeu");
    private readonly Button _btnRemoveGame;
    private readonly ContextMenuStrip _addMenu = new();
    private GameProfile? _shownGame;
    private readonly Control[] _centerRow;
    private readonly ComboBox _cmbMode, _cmbBlinks, _cmbComfortMs;
    private readonly Toggle _tglComfort = new("Appui bref = clignotements confort");
    private readonly Toggle _tglLights = new("Gérer les phares avec le commodo (bêta)");
    private readonly Label _lblLights, _lblTelemetry;
    private readonly LightLamp _lampParking, _lampLow, _lampHigh;
    private readonly FlowLayoutPanel _lightLamps;
    private readonly List<Control> _lightRows = [];
    private readonly Dictionary<Label, Func<ButtonBinding?>> _lightLabels = [];
    private readonly Label _lblKeyCycle, _lblKeyHigh, _lblKeyFlash;
    private readonly List<Control> _gameLightRows = [];
    private Control[] _highRow = [];
    private readonly Toggle _tglMinimized = new("Démarrer réduit dans la zone de notification");

    private int S(int px) => px * DeviceDpi / 96;

    public MainForm()
    {
        Text = "StalkSync";
        Icon = LoadIcon(new Size(32, 32));
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;
        Padding = new Padding(S(20), S(16), S(20), S(8));

        // Trois colonnes de cartes : la fenêtre tient en hauteur sur un écran 1080p.
        int col = S(400), gap = S(14), width = col * 3 + gap * 2;
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            BackColor = Theme.Background, Margin = Padding.Empty,
        };
        root.Location = new Point(Padding.Left, Padding.Top);
        Controls.Add(root);

        // En-tête : nom et état du jeu.
        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, S(12)) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width - S(240)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(240)));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        titles.Controls.Add(WithMargin(Theme.Label("StalkSync", Theme.Title), Padding.Empty));
        titles.Controls.Add(WithMargin(Theme.Label("Clignotants réalistes pour ETS2, ATS, BeamNG, Assetto Corsa…", Theme.Small, Theme.Muted), Padding.Empty));
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_pill, 1, 0);
        root.Controls.Add(header);

        var columns = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty,
        };
        FlowLayoutPanel Column(bool last) => new()
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            Margin = new Padding(0, 0, last ? 0 : gap, 0),
        };
        FlowLayoutPanel col1 = Column(false), col2 = Column(false), col3 = Column(true);
        columns.Controls.AddRange([col1, col2, col3]);
        root.Controls.Add(columns);

        // Tableau de bord : les deux voyants et l'état du commodo.
        int lampCol = S(80);
        var dash = new Card(null, col, lampCol, 0, lampCol);
        _lampLeft = new Lamp(S(64)) { PointsLeft = true };
        _lampRight = new Lamp(S(64));
        _lblState = Centered(Theme.Heading, Theme.Text);
        _lblStalk = Centered(Theme.Body, Theme.Muted);
        _lblAction = Centered(Theme.Small, Theme.Muted);
        dash.Row(_lampLeft, _lblState, _lampRight);
        dash.RowAt(1, _lblStalk);
        dash.RowAt(1, _lblAction);
        _lblLights = Centered(Theme.Body, Theme.Muted);
        dash.RowAt(1, _lblLights);
        // Voyants de phares, comme sur un tableau de bord.
        _lampParking = new LightLamp(LampKind.Parking, S(40));
        _lampLow = new LightLamp(LampKind.Low, S(40));
        _lampHigh = new LightLamp(LampKind.High, S(40));
        _lightLamps = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, BackColor = Theme.Card, Anchor = AnchorStyles.None,
            Margin = new Padding(0, S(4), 0, 0),
        };
        _lightLamps.Controls.AddRange([_lampParking, _lampLow, _lampHigh]);
        dash.RowAt(1, _lightLamps);
        foreach (var lamp in new[] { _lampLeft, _lampRight })
            dash.Grid.SetRowSpan(lamp, 5);
        _lblWarning = Theme.Label("", Theme.Body, Theme.Warning);
        _lblWarning.Margin = new Padding(0, S(8), 0, 0);
        _lblWarning.Visible = false;
        dash.Row(_lblWarning);
        col1.Controls.Add(dash);

        // Commodo.
        int labelCol = S(140), buttonCol = S(96);
        var stalk = new Card("Commodo", col, labelCol, 0, buttonCol);
        _cmbMode = Theme.Combo(stalk.InnerWidth - labelCol);
        _cmbMode.Dock = DockStyle.Fill;
        _cmbMode.Items.AddRange(new object[]
        {
            "Maintenu — reste en position",
            "Impulsion — revient au centre",
            "Un bouton par position (Moza)",
        });
        stalk.Row(Theme.Label("Type"), _cmbMode);
        stalk.Grid.SetColumnSpan(_cmbMode, 2);
        _lblStalkLeft = BindingLabel();
        _lblStalkCenter = BindingLabel();
        _lblStalkRight = BindingLabel();
        stalk.Row(Theme.Label("Gauche"), _lblStalkLeft, Theme.Button("Détecter", (_, _) =>
            DetectStalk(_lblStalkLeft, "Pousse à fond à gauche…", b => _config.StalkLeft = b)));
        _centerRow = stalk.Row(Theme.Label("Centre"), _lblStalkCenter, Theme.Button("Détecter", (_, _) =>
            DetectStalk(_lblStalkCenter, "Ramène au centre…", b => _config.StalkCenter = b)));
        stalk.Row(Theme.Label("Droite"), _lblStalkRight, Theme.Button("Détecter", (_, _) =>
            DetectStalk(_lblStalkRight, "Pousse à fond à droite…", b => _config.StalkRight = b)));
        _lblDevice = Hint(stalk.InnerWidth, "");
        stalk.Row(_lblDevice);
        col2.Controls.Add(stalk);

        // Phares : un bouton par position du commodo (Moza : 1 à 6).
        var lightCard = new Card("Phares", col, labelCol, 0, buttonCol);
        lightCard.Row(_tglLights);
        _lightRows.AddRange(lightCard.Row(Theme.Label("Préréglage"),
            Theme.Label("Moza : boutons 1 à 6", Theme.Body, Theme.Muted), Theme.Button("Appliquer", (_, _) => MozaLights())));
        foreach (var (caption, prompt, get, set) in new (string, string, Func<ButtonBinding?>, Action<ButtonBinding>)[]
        {
            ("Phares éteints", "Tourne sur éteint…", () => _config.Lights.Off, b => _config.Lights.Off = b),
            ("Veilleuses", "Tourne sur veilleuses…", () => _config.Lights.Parking, b => _config.Lights.Parking = b),
            ("Croisement", "Tourne sur croisement…", () => _config.Lights.Low, b => _config.Lights.Low = b),
            ("Pleins phares", "Pousse le levier…", () => _config.Lights.High, b => _config.Lights.High = b),
            ("Appel de phare", "Tire le levier…", () => _config.Lights.Flash, b => _config.Lights.Flash = b),
            ("Retour au neutre", "Ramène le levier…", () => _config.Lights.Neutral, b => _config.Lights.Neutral = b),
        })
        {
            var label = BindingLabel();
            _lightLabels[label] = get;
            _lightRows.AddRange(lightCard.Row(Theme.Label(caption), label,
                Theme.Button("Détecter", (_, _) => DetectStalk(label, prompt, set))));
        }
        _tglLights.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _config.Lights.Enabled = _tglLights.Checked;
            _config.Save();
            ShowBindings();
        };
        col2.Controls.Add(lightCard);

        // Touches du jeu.
        // Jeux : chacun a ses touches de clignotant.
        var keys = new Card("Jeux", col, labelCol, 0, buttonCol);
        _cmbGame = Theme.Combo(keys.InnerWidth - buttonCol);
        _cmbGame.Dock = DockStyle.Fill;
        _cmbGame.Margin = new Padding(0, 3, S(8), 3);
        _cmbGame.SelectedIndexChanged += (_, _) => ShowGame();
        Button add = null!;
        add = Theme.Button("Ajouter…", (_, _) =>
        {
            FillAddMenu();
            _addMenu.Show(add, new Point(0, add.Height));
        });
        keys.RowAt(0, _cmbGame);
        keys.Grid.SetColumnSpan(_cmbGame, 2);
        keys.Grid.Controls.Add(add, 2, keys.Grid.RowCount - 1);
        _btnRemoveGame = Theme.Button("Retirer", (_, _) => RemoveGame());
        keys.RowAt(0, _tglGameEnabled);
        keys.Grid.SetColumnSpan(_tglGameEnabled, 2);
        keys.Grid.Controls.Add(_btnRemoveGame, 2, keys.Grid.RowCount - 1);
        _lblKeyLeft = BindingLabel();
        _lblKeyRight = BindingLabel();
        keys.Row(Theme.Label("Clignotant gauche"), _lblKeyLeft, Theme.Button("Définir", (_, _) => CaptureKey(true)));
        keys.Row(Theme.Label("Clignotant droit"), _lblKeyRight, Theme.Button("Définir", (_, _) => CaptureKey(false)));
        _lblKeyCycle = BindingLabel();
        _lblKeyHigh = BindingLabel();
        _lblKeyFlash = BindingLabel();
        _gameLightRows.AddRange(keys.Row(Theme.Label("Mode des feux"), _lblKeyCycle,
            Theme.Button("Définir", (_, _) => CaptureGameKey(_lblKeyCycle, (g, k) => g.KeyLightCycle = k))));
        _highRow = keys.Row(Theme.Label("Pleins phares"), _lblKeyHigh,
            Theme.Button("Définir", (_, _) => CaptureGameKey(_lblKeyHigh, (g, k) => g.KeyHighBeam = k)));
        _gameLightRows.AddRange(_highRow);
        _gameLightRows.AddRange(keys.Row(Theme.Label("Appel de phare"), _lblKeyFlash,
            Theme.Button("Définir", (_, _) => CaptureGameKey(_lblKeyFlash, (g, k) => g.KeyFlash = k))));
        _lblGameHint = Hint(keys.InnerWidth, "");
        keys.Row(_lblGameHint);
        _lblTelemetry = Hint(keys.InnerWidth, "");
        _lblTelemetry.Font = Theme.Body;
        keys.Row(_lblTelemetry);
        _tglGameEnabled.CheckedChanged += (_, _) =>
        {
            if (_loading || _shownGame == null) return;
            _shownGame.Enabled = _tglGameEnabled.Checked;
            _config.Save();
            FillGames();
        };
        col3.Controls.Add(keys);

        // Options.
        var options = new Card("Options", col, S(250), 0);
        _cmbBlinks = Theme.Combo(S(90));
        _cmbBlinks.Items.AddRange(Enumerable.Range(1, 9).Select(i => (object)i.ToString()).ToArray());
        _cmbComfortMs = Theme.Combo(S(90));
        _cmbComfortMs.Items.AddRange(ComfortDurations.Select(ms => (object)$"{ms} ms").ToArray());
        options.Row(_tglComfort);
        options.Row(Theme.Label("Nombre de clignotements"), _cmbBlinks);
        options.Row(Theme.Label("Durée max. de l'appui bref"), _cmbComfortMs);
        options.Row(_tglMinimized);
        col1.Controls.Add(options);

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        root.Controls.Add(WithMargin(Theme.Label(
            $"v{version} · les touches ne sont envoyées qu'au jeu au premier plan",
            Theme.Small, Theme.Muted), new Padding(S(4), 0, 0, S(8))));

        LoadSettings();
        foreach (var c in new[] { _cmbMode, _cmbBlinks, _cmbComfortMs }) c.SelectedIndexChanged += SettingsChanged;
        foreach (var t in new[] { _tglComfort, _tglMinimized }) t.CheckedChanged += SettingsChanged;
        _loading = false;

        _tray.Icon = LoadIcon(SystemInformation.SmallIconSize);
        _tray.Text = "StalkSync";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add("Ouvrir", null, (_, _) => ShowFromTray());
        _tray.ContextMenuStrip.Items.Add("Quitter", null, (_, _) => { _quitting = true; Close(); });

        _uiTimer.Tick += (_, _) => RefreshStatus();
    }

    private static Control WithMargin(Control c, Padding margin)
    {
        c.Margin = margin;
        return c;
    }

    private Label Centered(Font font, Color color) => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Height = TextRenderer.MeasureText("Ag", font).Height + S(6),
        TextAlign = ContentAlignment.MiddleCenter,
        Font = font,
        ForeColor = color,
        Margin = Padding.Empty,
        AutoEllipsis = true,
    };

    private Label BindingLabel()
    {
        var l = Theme.Label();
        l.AutoSize = false;
        l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleLeft;
        l.AutoEllipsis = true;
        l.Margin = new Padding(0, 0, S(8), 0);
        return l;
    }

    private static Label Hint(int width, string text)
    {
        var l = Theme.Label(text, Theme.Small, Theme.Muted);
        l.MaximumSize = new Size(width, 0);
        return l;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(Handle);
        _engine ??= new Engine(_config, Handle);
        _uiTimer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = null;
        if (_config.StartMinimized) Hide();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _uiTimer.Stop();
        _tray.Visible = false;
        _engine?.Dispose();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (_keyCapture is { } capture)
        {
            // On attend la touche principale : Ctrl, Alt et Maj seuls ne comptent pas.
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LControlKey or Keys.RControlKey
                or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu)
                return true;
            _keyCapture = null;
            if (key != Keys.Escape)
                capture(new KeyBinding
                {
                    ScanCode = (ushort)Native.MapVirtualKey((uint)key, 0),
                    Extended = ExtendedKeys.Contains(key),
                    Ctrl = keyData.HasFlag(Keys.Control),
                    Alt = keyData.HasFlag(Keys.Alt),
                    Shift = keyData.HasFlag(Keys.Shift),
                    Name = key.ToString(),
                });
            ShowBindings();
            return true;
        }
        if (key == Keys.Escape && _detecting != null)
        {
            _engine?.CancelDetect();
            _detecting = null;
            ShowBindings();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void LoadSettings()
    {
        var b = _config.Blinker;
        _cmbMode.SelectedIndex = Math.Clamp((int)b.Mode, 0, _cmbMode.Items.Count - 1);
        _tglComfort.Checked = b.ComfortEnabled;
        _tglLights.Checked = _config.Lights.Enabled;
        _cmbBlinks.SelectedIndex = Math.Clamp(b.ComfortBlinks, 1, 9) - 1;
        _cmbComfortMs.SelectedIndex = Array.IndexOf(ComfortDurations,
            ComfortDurations.MinBy(ms => Math.Abs(ms - b.ComfortMaxPressMs)));
        _tglMinimized.Checked = _config.StartMinimized;
        FillGames();
    }

    private void SettingsChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        var b = _config.Blinker;
        b.Mode = (StalkMode)_cmbMode.SelectedIndex;
        b.ComfortEnabled = _tglComfort.Checked;
        b.ComfortBlinks = _cmbBlinks.SelectedIndex + 1;
        b.ComfortMaxPressMs = ComfortDurations[_cmbComfortMs.SelectedIndex];
        _config.StartMinimized = _tglMinimized.Checked;
        _config.Save();
        _engine?.ResetController();
        ShowBindings();
    }

    private void ShowBindings()
    {
        static void Show(Label l, string? text)
        {
            l.Text = text ?? "Non configuré";
            l.ForeColor = text == null ? Theme.Muted : Theme.Text;
        }
        void ShowButton(Label l, ButtonBinding? b)
        {
            Show(l, b == null ? null : $"Bouton {string.Join(" + ", b.All.Select(i => i + 1))}");
            _tips.SetToolTip(l, b?.ToString());
        }
        ShowButton(_lblStalkLeft, _config.StalkLeft);
        ShowButton(_lblStalkCenter, _config.StalkCenter);
        ShowButton(_lblStalkRight, _config.StalkRight);
        _lblDevice.Text = (_config.StalkLeft ?? _config.StalkRight) is { } dev ? "Périphérique : " + dev.DeviceName : "";
        Show(_lblKeyLeft, _shownGame?.KeyLeft is { } kl ? Native.KeyName(kl) : null);
        Show(_lblKeyRight, _shownGame?.KeyRight is { } kr ? Native.KeyName(kr) : null);
        bool center = _config.Blinker.Mode == StalkMode.Positions;
        foreach (var c in _centerRow) c.Visible = center;
        bool lights = _config.Lights.Enabled;
        foreach (var c in _lightRows) c.Visible = lights;
        foreach (var (label, get) in _lightLabels) ShowButton(label, get());
        foreach (var c in _gameLightRows) c.Visible = lights;
        if (_shownGame is { } shown)
        {
            Show(_lblKeyCycle, shown.KeyLightCycle is { } kc ? Native.KeyName(kc) : null);
            Show(_lblKeyHigh, shown.KeyHighBeam is { } kh ? Native.KeyName(kh) : null);
            Show(_lblKeyFlash, shown.KeyFlash is { } kf ? Native.KeyName(kf) : null);
            if (lights && shown.HighInCycle)
                foreach (var c in _highRow) c.Visible = false;
        }
        _cmbBlinks.Enabled = _cmbComfortMs.Enabled = _config.Blinker.ComfortEnabled;
    }

    private void DetectStalk(Label label, string prompt, Action<ButtonBinding> assign)
    {
        _keyCapture = null;
        ShowBindings();
        _detecting = label;
        label.Text = prompt;
        label.ForeColor = Theme.Accent;
        _engine?.BeginDetect(binding => BeginInvoke(() =>
        {
            _detecting = null;
            assign(binding);
            _config.Save();
            _engine?.ResetController();
            ShowBindings();
        }));
    }

    private void CaptureKey(bool left) =>
        CaptureGameKey(left ? _lblKeyLeft : _lblKeyRight, (g, k) => { if (left) g.KeyLeft = k; else g.KeyRight = k; });

    private void CaptureGameKey(Label label, Action<GameProfile, KeyBinding> assign)
    {
        _engine?.CancelDetect();
        _detecting = null;
        ShowBindings();
        if (_shownGame is not { } game) return;
        label.Text = "Appuie sur la touche…";
        label.ForeColor = Theme.Accent;
        ActiveControl = null;
        _keyCapture = key =>
        {
            assign(game, key);
            _config.Save();
            FillGames();
        };
    }

    /// <summary>Commodo Moza : phares sur les boutons 1 à 6 du même périphérique que les clignotants.</summary>
    private void MozaLights()
    {
        var device = _config.StalkLeft ?? _config.StalkRight;
        if (device == null)
        {
            MessageBox.Show(this, "Configure d'abord les clignotants : le préréglage utilise le même commodo.",
                "StalkSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ButtonBinding B(int n) => new() { Device = device.Device, DeviceName = device.DeviceName, Button = n - 1 };
        var l = _config.Lights;
        (l.Off, l.Parking, l.Low, l.High, l.Neutral, l.Flash) = (B(1), B(2), B(3), B(4), B(5), B(6));
        _config.Save();
        ShowBindings();
    }

    /// <summary>Remplit la liste des jeux en gardant la sélection.</summary>
    private void FillGames()
    {
        var keep = _shownGame;
        bool loading = _loading;
        _loading = true;
        _cmbGame.BeginUpdate();
        _cmbGame.Items.Clear();
        foreach (var g in _config.Games)
            _cmbGame.Items.Add(g.Name + (!g.Enabled ? "  (désactivé)" : !g.Ready ? "  (touches à définir)" : ""));
        _cmbGame.EndUpdate();
        int index = keep == null ? 0 : Math.Max(0, _config.Games.IndexOf(keep));
        _cmbGame.SelectedIndex = Math.Min(index, _cmbGame.Items.Count - 1);
        _loading = loading;
        ShowGame();
    }

    private void ShowGame()
    {
        int i = _cmbGame.SelectedIndex;
        _shownGame = i >= 0 && i < _config.Games.Count ? _config.Games[i] : null;
        if (_shownGame == null) return;
        bool loading = _loading;
        _loading = true;
        _tglGameEnabled.Checked = _shownGame.Enabled;
        _loading = loading;
        _btnRemoveGame.Visible = !_shownGame.Builtin;
        _lblGameHint.Text = Config.Hint(_shownGame);
        _keyCapture = null;
        ShowBindings();
    }

    private void FillAddMenu()
    {
        _addMenu.Items.Clear();
        var known = _config.Games.SelectMany(g => g.Processes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var windows = Native.WindowedProcesses().Where(w => !known.Contains(w.Process)).ToList();
        if (windows.Count == 0)
        {
            _addMenu.Items.Add(new ToolStripMenuItem("Lance le jeu, puis reviens ici") { Enabled = false });
            return;
        }
        _addMenu.Items.Add(new ToolStripMenuItem("Choisis la fenêtre du jeu :") { Enabled = false });
        foreach (var (process, title) in windows)
        {
            string name = title.Length > 40 ? title[..40] + "…" : title;
            _addMenu.Items.Add($"{name}   ({process}.exe)", null, (_, _) => AddGame(name, process));
        }
    }

    private void AddGame(string name, string process)
    {
        var game = new GameProfile { Name = name, Processes = [process] };
        // Nouvelle liste plutôt que modification : le moteur la parcourt sur un autre thread.
        _config.Games = [.. _config.Games, game];
        _config.Save();
        _shownGame = game;
        FillGames();
    }

    private void RemoveGame()
    {
        if (_shownGame is not { Builtin: false } game) return;
        _config.Games = _config.Games.Where(g => g != game).ToList();
        _config.Save();
        _shownGame = null;
        FillGames();
    }

    private void RefreshStatus()
    {
        if (!Visible || _engine?.Status is not { } s) return;

        _pill.Set(s.Game is { } focused
                ? "▶  " + Config.ShortName(focused) + (Config.IsBeamNg(focused) && s.Telemetry != null ? " · télémétrie" : "")
                : "Aucun jeu au premier plan",
            s.Game != null ? Theme.Accent : Theme.Muted);

        bool phase = Environment.TickCount64 / 400 % 2 == 0;
        // Avec la télémétrie, les voyants clignotent au rythme réel du jeu.
        _lampLeft.Lit = s.Telemetry is { } tl ? tl.LeftLit : s.Assumed == Side.Left && phase;
        _lampRight.Lit = s.Telemetry is { } tr ? tr.RightLit : s.Assumed == Side.Right && phase;

        _lblState.Text = s.Desired switch
        {
            Side.Left => "Clignotant gauche",
            Side.Right => "Clignotant droit",
            _ => "Clignotants éteints",
        } + (s.Comfort ? " · confort" : "");
        _lblStalk.Text = "Commodo " + s.Stalk switch
        {
            Side.Left => "à gauche",
            Side.Right => "à droite",
            _ => "au centre",
        } + (s.Desired != s.Assumed && s.Game == null ? " · en attente du jeu" : "");
        _lblAction.Text = s.LastAction == "—" ? "Aucune touche envoyée" : "Dernière touche : " + s.LastAction;
        _lblLights.Visible = _lightLamps.Visible = s.LightsEnabled;
        if (s.LightsEnabled)
        {
            // État des feux dans le jeu : réel pour les pleins phares avec la télémétrie, supposé sinon.
            bool high = s.Telemetry is { } th ? th.High
                : s.ShownFlash || (s.ShownHighInCycle ? s.ShownCycle == LightState.High : s.ShownHigh && s.ShownCycle == LightState.Low);
            bool low = s.Telemetry is { Low: { } tlow } ? tlow : s.ShownCycle is LightState.Low or LightState.High;
            _lampParking.Lit = s.ShownCycle == LightState.Parking;
            _lampLow.Lit = low;
            _lampHigh.Lit = high;
        }
        if (s.LightsEnabled)
            _lblLights.Text = "Commodo : " + s.Ring switch
            {
                LightState.Parking => "veilleuses",
                LightState.Low => "croisement",
                _ => "éteints",
            } + s.Beam switch
            {
                BeamPosition.High => " · pleins phares",
                BeamPosition.Flash => " · appel de phare",
                _ => "",
            };

        _lblTelemetry.Visible = _shownGame != null && Config.IsBeamNg(_shownGame);
        if (_lblTelemetry.Visible)
        {
            (_lblTelemetry.Text, _lblTelemetry.ForeColor) = s.TelemetryError is { } err
                ? ($"Télémétrie OutGauge : {err}.", Theme.Warning)
                : s.Telemetry is { } tm
                    ? ($"Télémétrie OutGauge : reçue (voyants 0x{tm.ShowLights:X4}).", Theme.Accent)
                    : ($"Télémétrie OutGauge : en attente. Dans BeamNG : Options → Autres → Protocoles → OutGauge, "
                       + $"adresse 127.0.0.1, port {s.TelemetryPort}.", Theme.Muted);
        }

        string? warning = s.Devices == 0 ? "Aucun périphérique de jeu détecté."
            : s.Game is { Ready: false } g ? $"Touches de clignotant non définies pour {Config.ShortName(g)}."
            : _config.StalkLeft == null || _config.StalkRight == null ? "Commodo non configuré : clique sur Détecter ci-dessous."
            : _config.Blinker.Mode == StalkMode.Positions && _config.StalkCenter == null ? "Position centrale non configurée."
            : !s.LeftFound || !s.RightFound ? "Commodo introuvable : vérifie qu'il est branché."
            : null;
        _lblWarning.Visible = warning != null;
        if (warning != null) _lblWarning.Text = "⚠  " + warning;
    }

    /// <summary>Logo intégré à l'exe, à la taille demandée.</summary>
    private static Icon LoadIcon(Size size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StalkSync.ico")!;
        return new Icon(stream, size);
    }
}
