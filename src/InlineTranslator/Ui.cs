using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace InlineTranslator;

internal static class Ui
{
    public static readonly Color Canvas = Color.FromArgb(243, 245, 249);
    public static readonly Color Card = Color.White;
    public static readonly Color Line = Color.FromArgb(226, 230, 237);
    public static readonly Color Ink = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentSoft = Color.FromArgb(234, 241, 255);
    public static readonly Color Soft = Color.FromArgb(241, 243, 247);
    public static readonly Color SoftHover = Color.FromArgb(229, 233, 240);
    public static readonly Color Green = Color.FromArgb(22, 163, 74);
    public static readonly Color GreenInk = Color.FromArgb(21, 128, 61);
    public static readonly Color GreenSoft = Color.FromArgb(232, 247, 238);
    public static readonly Color Red = Color.FromArgb(220, 38, 38);
    public static readonly Color RedSoft = Color.FromArgb(253, 236, 236);
    public static readonly Color Gray = Color.FromArgb(156, 163, 175);

    public static Font Regular(float size = 9.5f) => new("Segoe UI", size);
    public static Font Bold(float size = 9.5f) => new("Segoe UI Semibold", size);

    public static int Scale(Control control, int value) => (int)Math.Round(value * control.DeviceDpi / 96d);

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color BackgroundOf(Control control)
    {
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent)
            if (parent.BackColor.A == 255) return parent.BackColor;
        return Canvas;
    }

    public static void FillRounded(Graphics graphics, Control control, Color fill, Color border, int radius)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackgroundOf(control));
        var bounds = new RectangleF(0.5f, 0.5f, control.Width - 1.5f, control.Height - 1.5f);
        using var path = Rounded(bounds, Scale(control, radius));
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        if (border.A == 0) return;
        using var pen = new Pen(border);
        graphics.DrawPath(pen, path);
    }

    public static void LayoutFooter(Control footer, Control? left, IEnumerable<Control> rightToLeftEnd)
    {
        int pad = Scale(footer, 20), gap = Scale(footer, 8);
        if (left is not null)
        {
            var size = left.GetPreferredSize(Size.Empty);
            left.SetBounds(pad, (footer.ClientSize.Height - size.Height) / 2, size.Width, size.Height);
        }
        int x = footer.ClientSize.Width - pad;
        foreach (var control in rightToLeftEnd.Reverse())
        {
            if (!control.Visible) continue;
            var size = control.GetPreferredSize(Size.Empty);
            int width = Math.Max(size.Width, control.MinimumSize.Width), height = Math.Max(size.Height, control.MinimumSize.Height);
            x -= width;
            control.SetBounds(x, (footer.ClientSize.Height - height) / 2, width, height);
            x -= gap;
        }
    }

    public static void RoundWindow(Control window)
    {
        int round = 2;
        _ = Native.DwmSetWindowAttribute(window.Handle, 33, ref round, sizeof(int));
    }

    public static Label Text(string value, Color? color = null, Font? font = null, int maxWidth = 0) => new()
    {
        Text = value,
        AutoSize = true,
        ForeColor = color ?? Ink,
        Font = font ?? Regular(),
        MaximumSize = new Size(maxWidth, 0),
        Margin = new Padding(0, 4, 8, 4),
        Anchor = AnchorStyles.Left,
        BackColor = Color.Transparent,
    };
}

internal sealed class RoundedPanel : Panel
{
    private readonly Color fill;
    private readonly Color border;
    private readonly int radius;

    public RoundedPanel(Color fill, Color border, int radius)
    {
        this.fill = fill;
        this.border = border;
        this.radius = radius;
        BackColor = fill;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e) => Ui.FillRounded(e.Graphics, this, fill, border, radius);
}

internal enum PillKind { Primary, Secondary, Soft, Danger }

internal sealed class PillButton : Button
{
    private readonly PillKind kind;
    private bool hovered, pressed;

    public PillButton(string text, PillKind kind)
    {
        this.kind = kind;
        Text = text;
        AutoSize = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = kind == PillKind.Primary ? Ui.Bold() : Ui.Regular();
        Padding = new Padding(14, 4, 14, 4);
        MinimumSize = new Size(0, 32);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        return new Size(text.Width + Padding.Horizontal, Math.Max(MinimumSize.Height, text.Height + Padding.Vertical));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool active = hovered || pressed;
        (Color fill, Color ink) = kind switch
        {
            PillKind.Primary => (active ? Ui.AccentHover : Ui.Accent, Color.White),
            PillKind.Soft => (active ? Color.FromArgb(219, 231, 255) : Ui.AccentSoft, Ui.Accent),
            PillKind.Danger => (active ? Color.FromArgb(250, 220, 220) : Ui.RedSoft, Ui.Red),
            _ => (active ? Ui.SoftHover : Ui.Soft, Ui.Ink),
        };
        if (!Enabled) (fill, ink) = (Ui.Soft, Ui.Gray);
        Ui.FillRounded(e.Graphics, this, fill, Focused && ShowFocusCues ? Ui.Accent : Color.Transparent, Height / 2);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    [DefaultValue(false)]
    public bool WrapText { get; set; }

    public ToggleSwitch(string text)
    {
        Text = text;
        AutoSize = false;
        Font = Ui.Regular();
        ForeColor = Ui.Ink;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 4, 0, 4);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        Size = GetPreferredSize(Size.Empty);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int track = Ui.Scale(this, 38);
        if (WrapText && proposedSize.Width > 0)
        {
            int gap = Ui.Scale(this, 12);
            int width = Math.Max(1, proposedSize.Width - track - gap);
            var wrapped = TextRenderer.MeasureText(Text, Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return new Size(proposedSize.Width, Math.Max(Ui.Scale(this, 26), wrapped.Height + Ui.Scale(this, 8)));
        }
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        return new Size(track + (Text.Length > 0 ? Ui.Scale(this, 10) + text.Width : 0), Math.Max(Ui.Scale(this, 26), text.Height + 4));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.BackgroundOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = Ui.Scale(this, 38), h = Ui.Scale(this, 22);
        float top = (Height - h) / 2f;
        var track = new RectangleF(0.5f, top, w - 1, h);
        Color on = Enabled ? Ui.Accent : Color.FromArgb(160, 185, 235);
        Color off = Enabled ? Color.FromArgb(203, 209, 219) : Ui.Line;
        using (var path = Ui.Rounded(track, h / 2f))
        using (var brush = new SolidBrush(Checked ? on : off))
            g.FillPath(brush, path);
        float knob = h - Ui.Scale(this, 6);
        float x = Checked ? w - knob - Ui.Scale(this, 3) : Ui.Scale(this, 3);
        using (var brush = new SolidBrush(Color.White))
            g.FillEllipse(brush, x, top + Ui.Scale(this, 3), knob, knob);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Ui.Accent);
            using var ring = Ui.Rounded(new RectangleF(0.5f, top - 1, w - 1, h + 2), h / 2f + 1);
            g.DrawPath(pen, ring);
        }
        if (Text.Length == 0) return;
        var textBounds = new Rectangle(w + Ui.Scale(this, 10), 0, Width - w - Ui.Scale(this, 10), Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? ForeColor : Ui.Gray,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix
            | (WrapText ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine));
    }
}

internal sealed class PillDropdown : Control
{
    private readonly ContextMenuStrip menu = new() { ShowImageMargin = false, Renderer = new RoundMenuRenderer() };
    private readonly List<(object Value, string Label)> options = [];
    private bool hovered, open;
    private int selected = -1;
    private Func<string?>? customPrompt;
    private string customLabel = "";

    public event Action? SelectionChanged;
    [DefaultValue(false)]
    public bool WrapText { get; set; }

    public PillDropdown(int width)
    {
        Width = width;
        Height = 34;
        Font = Ui.Regular();
        ForeColor = Ui.Ink;
        Cursor = Cursors.Hand;
        TabStop = true;
        Margin = new Padding(0, 3, 8, 3);
        menu.Font = Ui.Regular();
        menu.Opened += (_, _) => { open = true; Ui.RoundWindow(menu); Invalidate(); };
        menu.Closed += (_, _) => { open = false; Invalidate(); };
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Disposed += (_, _) => menu.Dispose();
    }

    public object? SelectedValue => selected >= 0 ? options[selected].Value : null;
    public string SelectedLabel => selected >= 0 ? options[selected].Label : "";
    public int SelectedIndex => selected;

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!WrapText) return base.GetPreferredSize(proposedSize);
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int padding = Ui.Scale(this, 12), chevron = Ui.Scale(this, 10);
        string label = SelectedLabel;
        var measured = TextRenderer.MeasureText(label, Font,
            new Size(Math.Max(1, width - padding * 2 - chevron), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return new Size(width, Math.Max(Ui.Scale(this, 36), measured.Height + Ui.Scale(this, 16)));
    }

    public void SetOptions(IEnumerable<(object Value, string Label)> items)
    {
        options.Clear();
        options.AddRange(items);
        selected = options.Count > 0 ? Math.Clamp(selected, 0, options.Count - 1) : -1;
        Invalidate();
    }

    public void AllowCustom(string label, Func<string?> prompt)
    {
        customLabel = label;
        customPrompt = prompt;
    }

    public void Select(object value)
    {
        object? previous = SelectedValue;
        int index = options.FindIndex(option => Equals(option.Value, value));
        if (index < 0)
        {
            options.Add((value, value.ToString() ?? ""));
            index = options.Count - 1;
        }
        selected = index;
        Invalidate();
        if (!Equals(previous, SelectedValue)) SelectionChanged?.Invoke();
    }

    public void SelectIndex(int index)
    {
        int previous = selected;
        selected = Math.Clamp(index, -1, options.Count - 1);
        Invalidate();
        if (previous != selected) SelectionChanged?.Invoke();
    }

    private void Choose(int index)
    {
        if (index == selected) return;
        selected = index;
        Invalidate();
        SelectionChanged?.Invoke();
    }

    private void OpenMenu()
    {
        if (!Enabled || options.Count == 0 && customPrompt is null) return;
        menu.Items.Clear();
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(options[i].Label) { Checked = i == selected, Padding = new Padding(4, 3, 4, 3) };
            item.Click += (_, _) => Choose(index);
            menu.Items.Add(item);
        }
        if (customPrompt is not null)
        {
            menu.Items.Add(new ToolStripSeparator());
            var custom = new ToolStripMenuItem(customLabel) { ForeColor = Ui.Accent };
            custom.Click += (_, _) =>
            {
                if (customPrompt() is { Length: > 0 } value)
                {
                    Select(value);
                }
            };
            menu.Items.Add(custom);
        }
        menu.MinimumSize = new Size(Width, 0);
        menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); OpenMenu(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Down or Keys.Up or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F4) OpenMenu();
        else if (e.KeyCode == Keys.Down && selected < options.Count - 1) Choose(selected + 1);
        else if (e.KeyCode == Keys.Up && selected > 0) Choose(selected - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color border = !Enabled ? Ui.Line : open || Focused ? Ui.Accent : hovered ? Color.FromArgb(190, 198, 210) : Ui.Line;
        Ui.FillRounded(g, this, Enabled ? Color.White : Ui.Soft, border, 10);
        int pad = Ui.Scale(this, 12), chevron = Ui.Scale(this, 10);
        string label = SelectedLabel;
        var textBounds = new Rectangle(pad, 0, Width - pad * 2 - chevron, Height);
        TextRenderer.DrawText(g, label, Font, textBounds, Enabled ? ForeColor : Ui.Gray,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix
            | (WrapText ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
        float cx = Width - pad - chevron / 2f, cy = Height / 2f;
        using var pen = new Pen(Enabled ? Ui.Muted : Ui.Gray, Ui.Scale(this, 2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = chevron / 3f;
        g.DrawLines(pen, [new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2)]);
    }
}

internal sealed class StatusPill : Control
{
    private Color dot = Ui.Gray, fill = Ui.Soft, ink = Ui.Muted;

    public StatusPill()
    {
        Font = Ui.Regular(9f);
        Height = 26;
        Margin = new Padding(0, 3, 6, 3);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
    }

    public void Show(string text, Color dotColor, Color fillColor, Color inkColor)
    {
        Text = text;
        (dot, fill, ink) = (dotColor, fillColor, inkColor);
        var size = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        Width = size.Width + Ui.Scale(this, 30);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int padding = Ui.Scale(this, 30);
        int natural = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine).Width + padding;
        int width = proposedSize.Width > 0 ? Math.Min(natural, proposedSize.Width) : natural;
        var text = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width - padding), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return new Size(width, Math.Max(Ui.Scale(this, 26), text.Height + Ui.Scale(this, 8)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.FillRounded(g, this, fill, Color.Transparent, Height / 2);
        int d = Ui.Scale(this, 8), left = Ui.Scale(this, 10);
        using (var brush = new SolidBrush(dot))
            g.FillEllipse(brush, left, (Height - d) / 2f, d, d);
        var bounds = new Rectangle(left + d + Ui.Scale(this, 6), 0, Width - left - d, Height);
        TextRenderer.DrawText(g, Text, Font, bounds, ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

internal sealed class Chip : Control
{
    private bool hoverClose;

    public event Action? Removed;

    public Chip(string text)
    {
        Text = text;
        Font = Ui.Regular(9f);
        Height = 28;
        Margin = new Padding(0, 3, 6, 3);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        Width = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.SingleLine).Width + Ui.Scale(this, 40);
    }

    private Rectangle CloseBounds => new(Width - Ui.Scale(this, 26), 0, Ui.Scale(this, 22), Height);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool inside = CloseBounds.Contains(e.Location);
        Cursor = inside ? Cursors.Hand : Cursors.Default;
        if (inside == hoverClose) return;
        hoverClose = inside;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverClose = false; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (CloseBounds.Contains(e.Location)) Removed?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.FillRounded(g, this, Ui.AccentSoft, Color.Transparent, Height / 2);
        var textBounds = new Rectangle(Ui.Scale(this, 12), 0, Width - Ui.Scale(this, 38), Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Ui.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        var close = CloseBounds;
        float cx = close.Left + close.Width / 2f, cy = Height / 2f, s = Ui.Scale(this, 4);
        if (hoverClose)
            using (var brush = new SolidBrush(Color.FromArgb(214, 226, 252)))
                g.FillEllipse(brush, cx - s * 2.2f, cy - s * 2.2f, s * 4.4f, s * 4.4f);
        using var pen = new Pen(Ui.Accent, Ui.Scale(this, 2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
        g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
    }
}

internal sealed class RoundMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Color.White);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Ui.Line);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Rounded(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 7);
        using var brush = new SolidBrush(Ui.AccentSoft);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = e.ImageRectangle.Left + e.ImageRectangle.Width / 2f, cy = e.ImageRectangle.Top + e.ImageRectangle.Height / 2f;
        using var pen = new Pen(Ui.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, [new PointF(cx - 5, cy), new PointF(cx - 1.5f, cy + 3.5f), new PointF(cx + 5, cy - 4)]);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? e.Item.ForeColor == SystemColors.ControlText ? Ui.Ink : e.Item.ForeColor : Ui.Gray;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Ui.Line);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }
}

internal static class InputDialog
{
    public static string? Ask(IWin32Window owner, string title, string prompt, string ok, string cancel)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Ui.Canvas,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18),
            Font = Ui.Regular(),
        };
        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
        var field = new RoundedPanel(Color.White, Ui.Line, 10) { Width = 320, Height = 36, Padding = new Padding(10, 9, 10, 6), Margin = new Padding(0, 6, 0, 12) };
        var box = new TextBox { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Font = Ui.Regular(10f) };
        field.Controls.Add(box);
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        var okButton = new PillButton(ok, PillKind.Primary) { DialogResult = DialogResult.OK };
        var cancelButton = new PillButton(cancel, PillKind.Secondary) { DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, 8, 0) };
        buttons.Controls.AddRange([okButton, cancelButton]);
        layout.Controls.AddRange([Ui.Text(prompt, Ui.Muted, maxWidth: 320), field, buttons]);
        form.Controls.Add(layout);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        return form.ShowDialog(owner) == DialogResult.OK && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}

[DesignerCategory("Code")]
internal sealed class FlowRow : FlowLayoutPanel
{
    public FlowRow()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Margin = Padding.Empty;
        BackColor = Color.Transparent;
    }
}

internal sealed class FluidStack : Panel
{
    private readonly bool card;

    public FluidStack(bool card = false)
    {
        this.card = card;
        BackColor = card ? Ui.Card : Color.Transparent;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    internal static Size Measure(Control control, int width)
    {
        width = Math.Max(1, width);
        if (control is RoundedPanel) return new Size(width, Math.Max(control.MinimumSize.Height, control.Height));
        if (control is FlowLayoutPanel flow)
        {
            int x = 0, height = 0, rowHeight = 0;
            foreach (Control child in flow.Controls)
            {
                var size = child.GetPreferredSize(new Size(Math.Max(1, width - child.Margin.Horizontal), 0));
                int childWidth = Math.Min(width, Math.Max(size.Width, child.MinimumSize.Width) + child.Margin.Horizontal);
                int childHeight = Math.Max(size.Height, child.MinimumSize.Height) + child.Margin.Vertical;
                if (x > 0 && x + childWidth > width)
                {
                    height += rowHeight;
                    x = rowHeight = 0;
                }
                child.Size = new Size(Math.Max(1, childWidth - child.Margin.Horizontal),
                    Math.Max(1, childHeight - child.Margin.Vertical));
                x += childWidth;
                rowHeight = Math.Max(rowHeight, childHeight);
            }
            return new Size(width, height + rowHeight);
        }
        var preferred = control.GetPreferredSize(new Size(width, 0));
        return new Size(Math.Min(width, Math.Max(preferred.Width, control.MinimumSize.Width)),
            Math.Max(preferred.Height, control.MinimumSize.Height));
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is Label label)
        {
            label.AutoSize = false;
            label.Margin = new Padding(0, 4, 0, 4);
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = Math.Max(1, proposedSize.Width > 0 ? proposedSize.Width : Width);
        int height = Padding.Vertical;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            height += Measure(child, width - Padding.Horizontal - child.Margin.Horizontal).Height + child.Margin.Vertical;
        }
        return new Size(width, height);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int y = Padding.Top;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            int width = Math.Max(1, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal);
            var size = Measure(child, width);
            int childWidth = child is PillButton or StatusPill ? size.Width : width;
            child.SetBounds(Padding.Left + child.Margin.Left, y + child.Margin.Top, childWidth, size.Height);
            child.PerformLayout();
            y += size.Height + child.Margin.Vertical;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (card) Ui.FillRounded(e.Graphics, this, Ui.Card, Ui.Line, 14);
        else base.OnPaint(e);
    }
}

internal sealed class FluidRow : Panel
{
    private readonly Label label;
    private readonly Control input;

    public FluidRow(Label label, Control input)
    {
        this.label = label;
        this.input = input;
        BackColor = Color.Transparent;
        Margin = new Padding(0, 4, 0, 4);
        label.AutoSize = false;
        label.Margin = input.Margin = Padding.Empty;
        Controls.AddRange([label, input]);
    }

    private bool SideBySide(int width) => width >= Math.Max(480, Ui.Scale(this, 240));

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = Math.Max(1, proposedSize.Width > 0 ? proposedSize.Width : Width);
        int gap = Ui.Scale(this, 8);
        int labelWidth = SideBySide(width) ? Ui.Scale(this, 90) : width;
        int labelHeight = label.GetPreferredSize(new Size(labelWidth, 0)).Height;
        int inputHeight = FluidStack.Measure(input, SideBySide(width) ? width - labelWidth - gap : width).Height;
        return new Size(width, SideBySide(width) ? Math.Max(labelHeight, inputHeight) : labelHeight + gap + inputHeight);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int width = Math.Max(1, ClientSize.Width), gap = Ui.Scale(this, 8);
        if (SideBySide(width))
        {
            int labelWidth = Ui.Scale(this, 90);
            label.SetBounds(0, 0, labelWidth, Height);
            input.SetBounds(labelWidth + gap, 0, Math.Max(1, width - labelWidth - gap), Height);
        }
        else
        {
            int labelHeight = label.GetPreferredSize(new Size(width, 0)).Height;
            label.SetBounds(0, 0, width, labelHeight);
            input.SetBounds(0, labelHeight + gap, width, Math.Max(1, Height - labelHeight - gap));
        }
        input.PerformLayout();
    }
}
