using System.Diagnostics;
using System.Reflection;

namespace StalkSync.Debugging;

public sealed class DebugForm : Form
{
    private const int MaxMinutes = 10;

    private readonly string _version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private Recorder? _recorder;

    private readonly Pill _pill = new();
    private readonly Button _btnRecord;
    private readonly Label _lblTime, _lblTelemetry, _lblButtons, _lblKeys, _lblEvents, _lblResult;
    private readonly TextBox _txtMarker;
    private readonly Button _btnMarker;

    private int S(int px) => px * DeviceDpi / 96;

    public DebugForm()
    {
        Text = "StalkSync Debug";
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StalkSync.ico")!)
            Icon = new Icon(stream, new Size(32, 32));
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(S(20), S(16), S(20), S(12));

        int width = S(460);
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            BackColor = Theme.Background, Margin = Padding.Empty, Location = new Point(Padding.Left, Padding.Top),
        };
        Controls.Add(root);

        var header = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, S(12)) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width - S(170)));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(170)));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        titles.Controls.Add(NoMargin(Theme.Label("StalkSync Debug", Theme.Title)));
        titles.Controls.Add(NoMargin(Theme.Label("Rapport de diagnostic à envoyer", Theme.Small, Theme.Muted)));
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_pill, 1, 0);
        root.Controls.Add(header);

        var how = new Card("Comment faire", width, 0);
        how.Row(Hint(how.InnerWidth,
            "1. Lance StalkSync et le jeu.\n" +
            "2. Clique sur Démarrer, puis reproduis le problème en jeu.\n" +
            "3. Au moment du problème, écris ce que tu vois et clique sur Repère.\n" +
            "4. Clique sur Arrêter : le rapport .json est créé sur le Bureau. Envoie-le."));
        how.Row(Hint(how.InnerWidth,
            "Enregistré : boutons du commodo, télémétrie BeamNG, décisions de StalkSync, jeu au premier plan et "
            + "touches arrivées dans un jeu (rien de ce que tu tapes ailleurs)."));
        root.Controls.Add(how);

        var rec = new Card("Enregistrement", width, S(170), 0);
        _btnRecord = Theme.Button("Démarrer", (_, _) => ToggleRecording());
        _btnRecord.Anchor = AnchorStyles.Left;
        _btnRecord.Font = Theme.Strong;
        _lblTime = Theme.Label("00:00", Theme.Heading, Theme.Muted);
        rec.Row(_btnRecord, _lblTime);
        _lblTelemetry = Value();
        _lblButtons = Value();
        _lblKeys = Value();
        _lblEvents = Value();
        rec.Row(Theme.Label("Télémétrie BeamNG"), _lblTelemetry);
        rec.Row(Theme.Label("Boutons"), _lblButtons);
        rec.Row(Theme.Label("Touches StalkSync"), _lblKeys);
        rec.Row(Theme.Label("Événements StalkSync"), _lblEvents);
        root.Controls.Add(rec);

        var marker = new Card("Repère", width, 0, S(90));
        _txtMarker = new TextBox
        {
            BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Body, Dock = DockStyle.Fill, Margin = new Padding(0, S(4), S(8), S(4)),
            PlaceholderText = "ex. : les pleins phares restent allumés",
        };
        _txtMarker.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddMarker(); } };
        _btnMarker = Theme.Button("Repère", (_, _) => AddMarker());
        marker.Row(_txtMarker, _btnMarker);
        root.Controls.Add(marker);

        _lblResult = Hint(width, "");
        _lblResult.Font = Theme.Body;
        root.Controls.Add(_lblResult);
        root.Controls.Add(NoMargin(Theme.Label($"v{_version}", Theme.Small, Theme.Muted)));

        _timer.Tick += (_, _) => Refresh();
        ShowIdle();
    }

    private static Control NoMargin(Control c)
    {
        c.Margin = Padding.Empty;
        return c;
    }

    private Label Value()
    {
        var l = Theme.Label("—");
        l.AutoSize = false;
        l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleLeft;
        l.AutoEllipsis = true;
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
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_recorder != null)
        {
            var answer = MessageBox.Show(this, "Un enregistrement est en cours. Enregistrer le rapport avant de quitter ?",
                "StalkSync Debug", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
            if (answer == DialogResult.Yes) StopRecording();
            _recorder?.Dispose();
        }
        base.OnFormClosing(e);
    }

    private void ToggleRecording()
    {
        if (_recorder == null) StartRecording();
        else StopRecording();
    }

    private void StartRecording()
    {
        _recorder = new Recorder(Handle);
        _btnRecord.Text = "Arrêter et créer le rapport";
        _lblResult.Text = "";
        _txtMarker.Enabled = _btnMarker.Enabled = true;
        _pill.Set("●  Enregistrement", Theme.Warning);
        _timer.Start();
    }

    private void StopRecording()
    {
        if (_recorder is not { } recorder) return;
        _timer.Stop();
        _recorder = null;
        try
        {
            var file = recorder.Save(_version);
            _lblResult.Text = $"Rapport créé : {file}";
            _lblResult.ForeColor = Theme.Accent;
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lblResult.Text = "Impossible d'écrire le rapport : " + ex.Message;
            _lblResult.ForeColor = Theme.Warning;
        }
        finally
        {
            recorder.Dispose();
        }
        ShowIdle();
    }

    private void AddMarker()
    {
        if (_recorder == null) return;
        var note = _txtMarker.Text.Trim();
        _recorder.AddMarker(note.Length > 0 ? note : "repère");
        _txtMarker.Clear();
        _lblResult.Text = $"Repère ajouté à {_recorder.Elapsed:mm\\:ss}.";
        _lblResult.ForeColor = Theme.Muted;
    }

    private void ShowIdle()
    {
        _btnRecord.Text = "Démarrer";
        _txtMarker.Enabled = _btnMarker.Enabled = false;
        _pill.Set("Prêt", Theme.Muted);
        _lblTime.Text = "00:00";
        foreach (var l in new[] { _lblTelemetry, _lblButtons, _lblKeys, _lblEvents }) l.Text = "—";
    }

    private new void Refresh()
    {
        if (_recorder is not { } r) return;
        _lblTime.Text = $"{r.Elapsed:mm\\:ss}";
        _lblTime.ForeColor = Theme.Warning;
        int rate = r.OgRate;
        _lblTelemetry.Text = rate > 0 ? $"{rate} paquets/s ({r.OgSource})" : "rien reçu pour l'instant";
        _lblTelemetry.ForeColor = rate > 0 ? Theme.Accent : Theme.Muted;
        _lblButtons.Text = $"{r.ButtonCount} changements · {r.DeviceCount} périphérique(s)";
        _lblKeys.Text = $"{r.InjectedKeyCount} appuis";
        _lblEvents.Text = r.StalkSyncEventCount > 0 ? r.StalkSyncEventCount.ToString() : "aucun (StalkSync est-il ouvert ?)";
        if (r.Elapsed.TotalMinutes >= MaxMinutes) StopRecording();
    }
}
