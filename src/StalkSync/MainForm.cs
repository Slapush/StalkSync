using System.Drawing.Drawing2D;
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
    private readonly Lamp _lampLeft, _lampRight;
    private readonly Label _lblState, _lblStalk, _lblAction, _lblWarning;
    private readonly Label _lblStalkLeft, _lblStalkCenter, _lblStalkRight, _lblKeyLeft, _lblKeyRight;
    private readonly Control[] _centerRow;
    private readonly ComboBox _cmbMode, _cmbBlinks, _cmbComfortMs;
    private readonly Toggle _tglComfort = new("Appui bref = clignotements confort");
    private readonly Toggle _tglMinimized = new("Démarrer réduit dans la zone de notification");

    private int S(int px) => px * DeviceDpi / 96;

    public MainForm()
    {
        Text = "StalkSync";
        Icon = MakeIcon();
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;
        Padding = new Padding(S(20), S(16), S(20), S(8));

        int width = S(560);
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            BackColor = Theme.Background, Margin = Padding.Empty,
        };
        root.Location = new Point(Padding.Left, Padding.Top);
        Controls.Add(root);

        // En-tête : nom et état du jeu.
        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, S(12)) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width - S(220)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(220)));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        titles.Controls.Add(WithMargin(Theme.Label("StalkSync", Theme.Title), Padding.Empty));
        titles.Controls.Add(WithMargin(Theme.Label("Clignotants réalistes pour ETS2 / ATS", Theme.Small, Theme.Muted), Padding.Empty));
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_pill, 1, 0);
        root.Controls.Add(header);

        // Tableau de bord : les deux voyants et l'état du commodo.
        int lampCol = S(100);
        var dash = new Card(null, width, lampCol, 0, lampCol);
        _lampLeft = new Lamp(S(76)) { PointsLeft = true };
        _lampRight = new Lamp(S(76));
        _lblState = Centered(Theme.Heading, Theme.Text);
        _lblStalk = Centered(Theme.Body, Theme.Muted);
        _lblAction = Centered(Theme.Small, Theme.Muted);
        dash.Row(_lampLeft, _lblState, _lampRight);
        dash.RowAt(1, _lblStalk);
        dash.RowAt(1, _lblAction);
        foreach (var lamp in new[] { _lampLeft, _lampRight })
            dash.Grid.SetRowSpan(lamp, 3);
        _lblWarning = Theme.Label("", Theme.Body, Theme.Warning);
        _lblWarning.Margin = new Padding(0, S(8), 0, 0);
        _lblWarning.Visible = false;
        dash.Row(_lblWarning);
        root.Controls.Add(dash);

        // Commodo.
        int labelCol = S(150), buttonCol = S(100);
        var stalk = new Card("Commodo", width, labelCol, 0, buttonCol);
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
        root.Controls.Add(stalk);

        // Touches du jeu.
        var keys = new Card("Touches du jeu", width, labelCol, 0, buttonCol);
        _lblKeyLeft = BindingLabel();
        _lblKeyRight = BindingLabel();
        keys.Row(Theme.Label("Clignotant gauche"), _lblKeyLeft, Theme.Button("Définir", (_, _) => CaptureKey(true)));
        keys.Row(Theme.Label("Clignotant droit"), _lblKeyRight, Theme.Button("Définir", (_, _) => CaptureKey(false)));
        keys.Row(Hint(keys.InnerWidth, "Les mêmes que dans Options → Touches d'ETS2. Retire le commodo des clignotants dans le jeu."));
        root.Controls.Add(keys);

        // Options.
        var options = new Card("Options", width, S(260), 0);
        _cmbBlinks = Theme.Combo(S(90));
        _cmbBlinks.Items.AddRange(Enumerable.Range(1, 9).Select(i => (object)i.ToString()).ToArray());
        _cmbComfortMs = Theme.Combo(S(90));
        _cmbComfortMs.Items.AddRange(ComfortDurations.Select(ms => (object)$"{ms} ms").ToArray());
        options.Row(_tglComfort);
        options.Row(Theme.Label("Nombre de clignotements"), _cmbBlinks);
        options.Row(Theme.Label("Durée max. de l'appui bref"), _cmbComfortMs);
        options.Row(_tglMinimized);
        root.Controls.Add(options);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        root.Controls.Add(WithMargin(Theme.Label(
            $"v{version?.ToString(3)} · les touches ne sont envoyées que quand ETS2 ou ATS est au premier plan",
            Theme.Small, Theme.Muted), new Padding(S(4), 0, 0, S(8))));

        LoadSettings();
        foreach (var c in new[] { _cmbMode, _cmbBlinks, _cmbComfortMs }) c.SelectedIndexChanged += SettingsChanged;
        foreach (var t in new[] { _tglComfort, _tglMinimized }) t.CheckedChanged += SettingsChanged;
        _loading = false;

        _tray.Icon = Icon;
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
        if (e.KeyCode == Keys.Escape && _detecting != null)
        {
            _engine?.CancelDetect();
            _detecting = null;
            ShowBindings();
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
        _cmbMode.SelectedIndex = Math.Clamp((int)b.Mode, 0, _cmbMode.Items.Count - 1);
        _tglComfort.Checked = b.ComfortEnabled;
        _cmbBlinks.SelectedIndex = Math.Clamp(b.ComfortBlinks, 1, 9) - 1;
        _cmbComfortMs.SelectedIndex = Array.IndexOf(ComfortDurations,
            ComfortDurations.MinBy(ms => Math.Abs(ms - b.ComfortMaxPressMs)));
        _tglMinimized.Checked = _config.StartMinimized;
        ShowBindings();
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
        Show(_lblStalkLeft, _config.StalkLeft?.ToString());
        Show(_lblStalkCenter, _config.StalkCenter?.ToString());
        Show(_lblStalkRight, _config.StalkRight?.ToString());
        Show(_lblKeyLeft, Native.KeyName(_config.KeyLeft));
        Show(_lblKeyRight, Native.KeyName(_config.KeyRight));
        bool center = _config.Blinker.Mode == StalkMode.Positions;
        foreach (var c in _centerRow) c.Visible = center;
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

    private void CaptureKey(bool left)
    {
        _engine?.CancelDetect();
        _detecting = null;
        ShowBindings();
        var label = left ? _lblKeyLeft : _lblKeyRight;
        label.Text = "Appuie sur la touche…";
        label.ForeColor = Theme.Accent;
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

        _pill.Set(s.GameFocused ? "Jeu au premier plan" : "Jeu en arrière-plan",
            s.GameFocused ? Theme.Accent : Theme.Muted);

        bool phase = Environment.TickCount64 / 400 % 2 == 0;
        _lampLeft.Lit = s.Assumed == Side.Left && phase;
        _lampRight.Lit = s.Assumed == Side.Right && phase;

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
        } + (s.Desired != s.Assumed && !s.GameFocused ? " · en attente du jeu" : "");
        _lblAction.Text = s.LastAction == "—" ? "Aucune touche envoyée" : "Dernière touche : " + s.LastAction;

        string? warning = s.Devices == 0 ? "Aucun périphérique de jeu détecté."
            : _config.StalkLeft == null || _config.StalkRight == null ? "Commodo non configuré : clique sur Détecter ci-dessous."
            : _config.Blinker.Mode == StalkMode.Positions && _config.StalkCenter == null ? "Position centrale non configurée."
            : !s.LeftFound || !s.RightFound ? "Commodo introuvable : vérifie qu'il est branché."
            : null;
        _lblWarning.Visible = warning != null;
        if (warning != null) _lblWarning.Text = "⚠  " + warning;
    }

    /// <summary>Icône dessinée au lancement : deux flèches vertes sur fond sombre.</summary>
    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = Theme.Rounded(new RectangleF(0, 0, 31, 31), 7);
            using var dark = new SolidBrush(Theme.Card);
            g.FillPath(dark, bg);
            using var green = new SolidBrush(Theme.Accent);
            g.FillPolygon(green, new PointF[] { new(3, 16), new(13, 7), new(13, 25) });
            g.FillPolygon(green, new PointF[] { new(29, 16), new(19, 7), new(19, 25) });
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
