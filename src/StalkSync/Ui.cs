using System.Drawing.Drawing2D;

namespace StalkSync;

/// <summary>Couleurs et polices de l'interface (thème sombre).</summary>
static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x14, 0x16, 0x1B);
    public static readonly Color Card = Color.FromArgb(0x1E, 0x22, 0x2A);
    public static readonly Color Border = Color.FromArgb(0x2C, 0x32, 0x3D);
    public static readonly Color Input = Color.FromArgb(0x27, 0x2C, 0x36);
    public static readonly Color InputHover = Color.FromArgb(0x31, 0x38, 0x44);
    public static readonly Color Text = Color.FromArgb(0xE8, 0xEA, 0xEE);
    public static readonly Color Muted = Color.FromArgb(0x8A, 0x93, 0xA3);
    public static readonly Color Accent = Color.FromArgb(0x3D, 0xDC, 0x84);
    public static readonly Color AccentDim = Color.FromArgb(0x24, 0x4A, 0x36);
    public static readonly Color Warning = Color.FromArgb(0xF5, 0xA6, 0x23);
    public static readonly Color LampOff = Color.FromArgb(0x33, 0x39, 0x45);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f);
    public static readonly Font Title = new("Segoe UI Semibold", 16f);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Label Label(string text = "", Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Body,
        ForeColor = color ?? Text,
        Margin = new Padding(0, 6, 8, 6),
    };

    public static Button Button(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Input,
            ForeColor = Text,
            Font = Body,
            Cursor = Cursors.Hand,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(0, 3, 0, 3),
            Anchor = AnchorStyles.Right,
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = InputHover;
        b.FlatAppearance.MouseDownBackColor = Border;
        b.Click += onClick;
        return b;
    }

    /// <summary>Liste déroulante dessinée à la main : la version Windows ignore le thème sombre.</summary>
    public static ComboBox Combo(int width)
    {
        var c = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            DrawMode = DrawMode.OwnerDrawFixed,
            BackColor = Input,
            ForeColor = Text,
            Font = Body,
            Width = width,
            MaxDropDownItems = 12,
            ItemHeight = TextRenderer.MeasureText("Ag", Body).Height + 4,
            Margin = new Padding(0, 3, 0, 3),
        };
        c.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using var bg = new SolidBrush(selected ? InputHover : Input);
            e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, c.GetItemText(c.Items[e.Index]), c.Font, e.Bounds, Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        return c;
    }
}

/// <summary>Carte à coins arrondis avec un titre et une grille de lignes.</summary>
sealed class Card : Panel
{
    public readonly TableLayoutPanel Grid;
    public readonly int InnerWidth;

    public Card(string? title, int width, params int[] columns)
    {
        DoubleBuffered = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Background;
        Padding = new Padding(16, 12, 16, 12);
        Margin = new Padding(0, 0, 0, 12);

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Theme.Card,
            Margin = Padding.Empty,
            // Le padding d'un Panel ne place pas ses enfants : on positionne la pile nous-mêmes.
            Location = new Point(Padding.Left, Padding.Top),
        };
        if (title != null)
            stack.Controls.Add(new Label
            {
                Text = title.ToUpperInvariant(),
                AutoSize = true,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Margin = new Padding(0, 0, 0, 6),
            });

        Grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Card,
            Margin = Padding.Empty,
            ColumnCount = columns.Length,
        };
        InnerWidth = width - Padding.Horizontal;
        int last = InnerWidth - columns.Where(c => c > 0).Sum();
        foreach (int c in columns)
            Grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, c > 0 ? c : last));
        stack.Controls.Add(Grid);
        Controls.Add(stack);
    }

    /// <summary>Ajoute une ligne ; un seul contrôle occupe toute la largeur.</summary>
    public Control[] Row(params Control[] controls)
    {
        int r = Grid.RowCount++;
        Grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int c = 0; c < controls.Length; c++)
        {
            Grid.Controls.Add(controls[c], c, r);
            if (controls.Length == 1) Grid.SetColumnSpan(controls[c], Grid.ColumnCount);
        }
        return controls;
    }

    /// <summary>Ajoute une ligne avec un seul contrôle, dans la colonne donnée.</summary>
    public void RowAt(int column, Control control)
    {
        int r = Grid.RowCount++;
        Grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Grid.Controls.Add(control, column, r);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10);
        using var fill = new SolidBrush(Theme.Card);
        using var pen = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>Voyant de clignotant façon tableau de bord.</summary>
sealed class Lamp : Control
{
    private bool _lit;
    public bool PointsLeft { get; init; }

    public Lamp(int size)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(size, size);
        BackColor = Theme.Card;
        Anchor = AnchorStyles.None;
        Margin = Padding.Empty;
    }

    public bool Lit
    {
        get => _lit;
        set { if (_lit != value) { _lit = value; Invalidate(); } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = Width, h = Height, m = w * 0.1f;
        using var halo = new SolidBrush(_lit ? Theme.AccentDim : Theme.Input);
        g.FillEllipse(halo, m, m, w - 2 * m, h - 2 * m);

        // Flèche pointant à droite, retournée pour la gauche.
        float cx = w / 2, cy = h / 2, len = w * 0.46f, shaft = h * 0.14f, head = h * 0.26f;
        var pts = new[]
        {
            new PointF(cx - len / 2, cy - shaft), new PointF(cx + len * 0.05f, cy - shaft),
            new PointF(cx + len * 0.05f, cy - head), new PointF(cx + len / 2, cy),
            new PointF(cx + len * 0.05f, cy + head), new PointF(cx + len * 0.05f, cy + shaft),
            new PointF(cx - len / 2, cy + shaft),
        };
        if (PointsLeft)
            for (int i = 0; i < pts.Length; i++) pts[i].X = w - pts[i].X;
        using var arrow = new SolidBrush(_lit ? Theme.Accent : Theme.LampOff);
        g.FillPolygon(arrow, pts);
    }
}

/// <summary>Interrupteur on/off.</summary>
sealed class Toggle : Control
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public Toggle(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        Text = text;
        Font = Theme.Body;
        ForeColor = Theme.Text;
        BackColor = Theme.Card;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 6, 0, 6);
        var size = TextRenderer.MeasureText(text, Font);
        Size = new Size(size.Height * 2 + 10 + size.Width + 4, Math.Max(size.Height, 20) + 2);
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) Checked = !Checked;
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = Height - 6, w = h * 1.8f, y = 3;
        using (var track = Theme.Rounded(new RectangleF(1, y, w, h), h / 2))
        using (var fill = new SolidBrush(_checked ? Theme.Accent : Theme.LampOff))
            g.FillPath(fill, track);
        float knob = h - 6, kx = _checked ? 1 + w - knob - 3 : 4;
        using (var white = new SolidBrush(Color.White))
            g.FillEllipse(white, kx, y + 3, knob, knob);
        if (Focused)
        {
            using var pen = new Pen(Theme.Muted) { DashStyle = DashStyle.Dot };
            using var outline = Theme.Rounded(new RectangleF(0, y - 1.5f, w + 2, h + 3), (h + 3) / 2);
            g.DrawPath(pen, outline);
        }
        var textRect = new Rectangle((int)(w + 10), 0, Width - (int)(w + 10), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

/// <summary>Pastille d'état arrondie avec un point de couleur.</summary>
sealed class Pill : Control
{
    private Color _color = Theme.Muted;

    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Font = Theme.Small;
        BackColor = Theme.Background;
        Anchor = AnchorStyles.Right;
    }

    public void Set(string text, Color color)
    {
        if (text == Text && color == _color) return;
        Text = text;
        _color = color;
        var size = TextRenderer.MeasureText(text, Font);
        Size = new Size(size.Width + size.Height + 22, size.Height + 10);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), (Height - 1) / 2f);
        using var fill = new SolidBrush(Color.FromArgb(40, _color));
        using var pen = new Pen(Color.FromArgb(110, _color));
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        float dot = Height * 0.32f;
        using var dotBrush = new SolidBrush(_color);
        g.FillEllipse(dotBrush, Height / 2f - dot / 2 + 2, (Height - dot) / 2, dot, dot);
        var r = new Rectangle((int)(Height / 2f + dot), 0, Width - (int)(Height / 2f + dot), Height);
        TextRenderer.DrawText(g, Text, Font, r, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }
}
