using StalkSync.Core;

namespace StalkSync;

public sealed class MainForm : Form
{
    private static readonly HashSet<Keys> ExtendedKeys = new()
    {
        Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Insert, Keys.Delete, Keys.Home, Keys.End,
        Keys.PageUp, Keys.PageDown, Keys.Divide, Keys.NumLock, Keys.RControlKey, Keys.RMenu,
    };

    private readonly Config _config = Config.Load();
    private Engine? _engine;
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 100 };
    private readonly NotifyIcon _tray = new();
    private bool _loading = true, _quitting;
    private Action<KeyBinding>? _keyCapture;

    private readonly Label _lblTelemetry = Value(), _lblGame = Value(), _lblBlinker = Value(),
        _lblStalk = Value(), _lblSteer = Value(), _lblAction = Value();
    private readonly Label _lblStalkLeft = Value(), _lblStalkRight = Value(),
        _lblKeyLeft = Value(), _lblKeyRight = Value();
    private readonly ComboBox _cmbMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    private readonly CheckBox _chkComfort = new() { Text = "Appui bref = clignotements confort", AutoSize = true };
    private readonly NumericUpDown _numBlinks = new() { Minimum = 1, Maximum = 9, Width = 50 };
    private readonly NumericUpDown _numComfortMs = new() { Minimum = 100, Maximum = 1500, Increment = 50, Width = 65 };
    private readonly CheckBox _chkAutoCancel = new() { Text = "Retour automatique après le virage (mode impulsion)", AutoSize = true };
    private readonly NumericUpDown _numSteer = new() { Minimum = 2, Maximum = 90, Width = 50 };
    private readonly CheckBox _chkMinimized = new() { Text = "Démarrer réduit dans la zone de notification", AutoSize = true };

    public MainForm()
    {
        Text = "StalkSync — clignotants ETS2 / ATS";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;
        Padding = new Padding(8);

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        Controls.Add(root);

        root.Controls.Add(Group("État",
            Row("Télémétrie", _lblTelemetry), Row("Jeu", _lblGame), Row("Clignotant (jeu)", _lblBlinker),
            Row("Commodo", _lblStalk), Row("Volant", _lblSteer), Row("Dernière action", _lblAction)));

        _cmbMode.Items.AddRange(new object[]
        {
            "Maintenu — le commodo reste en position",
            "Impulsion — le commodo revient au centre",
        });
        root.Controls.Add(Group("Commodo",
            Row("Gauche", _lblStalkLeft, Button("Détecter", (_, _) => DetectStalk(true))),
            Row("Droite", _lblStalkRight, Button("Détecter", (_, _) => DetectStalk(false))),
            Row("Type", _cmbMode)));

        root.Controls.Add(Group("Touches du jeu (identiques à celles réglées dans les options d'ETS2)",
            Row("Clignotant gauche", _lblKeyLeft, Button("Définir", (_, _) => CaptureKey(true))),
            Row("Clignotant droit", _lblKeyRight, Button("Définir", (_, _) => CaptureKey(false)))));

        root.Controls.Add(Group("Options",
            Row(_chkComfort),
            Row("Nombre de clignotements", _numBlinks),
            Row("Durée max. de l'appui bref (ms)", _numComfortMs),
            Row(_chkAutoCancel),
            Row("Seuil de braquage (%)", _numSteer),
            Row(_chkMinimized)));

        LoadSettings();
        foreach (Control c in new Control[] { _cmbMode, _chkComfort, _numBlinks, _numComfortMs, _chkAutoCancel, _numSteer, _chkMinimized })
        {
            if (c is ComboBox cb) cb.SelectedIndexChanged += SettingsChanged;
            if (c is CheckBox ch) ch.CheckedChanged += SettingsChanged;
            if (c is NumericUpDown n) n.ValueChanged += SettingsChanged;
        }
        _loading = false;

        _tray.Icon = SystemIcons.Application;
        _tray.Text = "StalkSync";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add("Ouvrir", null, (_, _) => ShowFromTray());
        _tray.ContextMenuStrip.Items.Add("Quitter", null, (_, _) => { _quitting = true; Close(); });

        _uiTimer.Tick += (_, _) => RefreshStatus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _engine ??= new Engine(_config, Handle);
        _uiTimer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_keyCapture is { } capture)
        {
            e.Handled = e.SuppressKeyPress = true;
            _keyCapture = null;
            if (e.KeyCode != Keys.Escape)
                capture(new KeyBinding
                {
                    ScanCode = (ushort)Native.MapVirtualKey((uint)e.KeyCode, 0),
                    Extended = ExtendedKeys.Contains(e.KeyCode),
                    Name = e.KeyCode.ToString(),
                });
            ShowBindings();
            return;
        }
        base.OnKeyDown(e);
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
        _cmbMode.SelectedIndex = b.Mode == StalkMode.Latching ? 0 : 1;
        _chkComfort.Checked = b.ComfortEnabled;
        _numBlinks.Value = Math.Clamp(b.ComfortBlinks, 1, 9);
        _numComfortMs.Value = Math.Clamp(b.ComfortMaxPressMs, 100, 1500);
        _chkAutoCancel.Checked = b.AutoCancelEnabled;
        _numSteer.Value = Math.Clamp((int)Math.Round(b.AutoCancelSteer * 100), 2, 90);
        _chkMinimized.Checked = _config.StartMinimized;
        ShowBindings();
    }

    private void SettingsChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        var b = _config.Blinker;
        b.Mode = _cmbMode.SelectedIndex == 0 ? StalkMode.Latching : StalkMode.Momentary;
        b.ComfortEnabled = _chkComfort.Checked;
        b.ComfortBlinks = (int)_numBlinks.Value;
        b.ComfortMaxPressMs = (int)_numComfortMs.Value;
        b.AutoCancelEnabled = _chkAutoCancel.Checked;
        b.AutoCancelSteer = (float)_numSteer.Value / 100f;
        _config.StartMinimized = _chkMinimized.Checked;
        _config.Save();
        _engine?.ResetController();
    }

    private void ShowBindings()
    {
        _lblStalkLeft.Text = _config.StalkLeft?.ToString() ?? "non défini";
        _lblStalkRight.Text = _config.StalkRight?.ToString() ?? "non défini";
        _lblKeyLeft.Text = _config.KeyLeft.Name;
        _lblKeyRight.Text = _config.KeyRight.Name;
    }

    private void DetectStalk(bool left)
    {
        var label = left ? _lblStalkLeft : _lblStalkRight;
        label.Text = "pousse le commodo à fond et garde-le 2 s…";
        _engine?.BeginDetect(binding => BeginInvoke(() =>
        {
            if (left) _config.StalkLeft = binding; else _config.StalkRight = binding;
            _config.Save();
            _engine?.ResetController();
            ShowBindings();
        }));
    }

    private void CaptureKey(bool left)
    {
        _engine?.CancelDetect();
        (left ? _lblKeyLeft : _lblKeyRight).Text = "appuie sur la touche… (Échap pour annuler)";
        ActiveControl = null;
        _keyCapture = key =>
        {
            if (left) _config.KeyLeft = key; else _config.KeyRight = key;
            _config.Save();
        };
    }

    private void RefreshStatus()
    {
        if (!Visible || _engine?.Status is not { } s) return;
        var g = s.Game;
        _lblTelemetry.Text = g.Connected
            ? "connectée" + (g.Paused ? " (jeu en pause)" : "")
            : "absente — mode sans retour d'état (plugin non installé ou jeu fermé)";
        _lblTelemetry.ForeColor = g.Connected ? Color.ForestGreen : Color.DarkOrange;
        _lblGame.Text = (s.GameId switch { 1 => "ETS2", 2 => "ATS", _ => "—" })
            + (s.GameFocused ? " — fenêtre active" : " — fenêtre inactive, aucune touche envoyée");
        _lblBlinker.Text = !g.Connected ? "inconnu"
            : g.Hazard ? "warnings"
            : g.LeftActive ? "gauche" + (g.LeftLit ? " ●" : " ○")
            : g.RightActive ? "droite" + (g.RightLit ? " ●" : " ○")
            : "éteint";
        string position = s.LeftPressed ? "gauche" : s.RightPressed ? "droite" : "centre";
        string wanted = s.Desired switch { Side.Left => "gauche", Side.Right => "droite", _ => "éteint" };
        _lblStalk.Text = !s.LeftFound && !s.RightFound
            ? $"introuvable ({s.Devices} périphérique(s) de jeu détecté(s))"
            : $"{position} → voulu : {wanted}{(s.Comfort ? " (confort)" : "")}";
        _lblSteer.Text = g.Connected ? $"{g.Steer * 100:+0;-0;0} % (positif = gauche), {s.SpeedKmh:0} km/h" : "—";
        _lblAction.Text = s.LastAction;
    }

    private static Label Value() => new() { AutoSize = true, Text = "—", Margin = new Padding(3, 6, 3, 3) };

    private static Button Button(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    private static Control[] Row(string caption, params Control[] controls) =>
        new Control[] { new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 6, 12, 3) } }
            .Concat(controls).ToArray();

    private static Control[] Row(Control single) => new[] { single };

    private static GroupBox Group(string title, params Control[][] rows)
    {
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Location = new Point(8, 22) };
        foreach (var row in rows)
        {
            int r = table.RowCount++;
            for (int c = 0; c < row.Length; c++)
            {
                table.Controls.Add(row[c], c, r);
                if (row.Length == 1) table.SetColumnSpan(row[c], 3);
            }
        }
        var box = new GroupBox { Text = title, AutoSize = true, MinimumSize = new Size(560, 0), Padding = new Padding(8) };
        box.Controls.Add(table);
        return box;
    }
}
