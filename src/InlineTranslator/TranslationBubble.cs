using System.Runtime.InteropServices;
using Accessibility;

namespace InlineTranslator;

internal static class FocusInspector
{
    private const uint ObjectIdClient = 0xFFFFFFFC;
    private const int StateReadOnly = 0x40;
    private static Guid accessibleInterface = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object? accessible);

    public static bool IsReadOnlyState(object? state) => state is int value && (value & StateReadOnly) != 0;

    // Reports read-only only when the app says so through accessibility; anything unknown counts as editable,
    // so the normal paste path stays the default.
    public static bool IsReadOnly(nint foreground)
    {
        uint thread = Native.GetWindowThreadProcessId(foreground, out _);
        var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf<Native.GuiThreadInfo>() };
        nint focus = thread != 0 && Native.GetGUIThreadInfo(thread, ref info) && info.Focus != 0 ? info.Focus : foreground;
        try
        {
            if (AccessibleObjectFromWindow(focus, ObjectIdClient, ref accessibleInterface, out object? root) != 0
                || root is not IAccessible current)
                return false;
            object child = 0;
            for (int depth = 0; depth < 12; depth++)
            {
                object? focused = current.accFocus;
                if (focused is IAccessible next)
                {
                    current = next;
                    continue;
                }
                if (focused is int id) child = id;
                break;
            }
            return IsReadOnlyState(current.get_accState(child));
        }
        catch (Exception error) when (error is COMException or InvalidCastException or ArgumentException
            or NotImplementedException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal sealed class TranslationBubble : Form
{
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExTransparent = 0x20;
    private readonly Label header = new() { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Regular(8.5f), Margin = new Padding(0, 0, 0, 4), BackColor = Color.Transparent };
    private readonly Label body = new() { AutoSize = true, ForeColor = Ui.Ink, Font = Ui.Regular(11f), MaximumSize = new Size(440, 0), Margin = Padding.Empty, BackColor = Color.Transparent };
    private readonly Label hint = new() { AutoSize = true, ForeColor = Ui.Gray, Font = Ui.Regular(8f), Margin = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
    private readonly System.Windows.Forms.Timer watcher = new() { Interval = 30 };
    private readonly bool[] held = new bool[256];

    public TranslationBubble()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 12, 16, 12);
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.White,
            Margin = Padding.Empty,
            Location = new Point(Padding.Left, Padding.Top),
        };
        stack.Controls.AddRange([header, body, hint]);
        Controls.Add(stack);
        watcher.Tick += (_, _) => CloseOnInput();
        Disposed += (_, _) => watcher.Dispose();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost | WsExTransparent;
            parameters.ClassStyle |= 0x00020000;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.RoundWindow(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Ui.Line);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    public void ShowNear(Point anchor, string title, string text, string closeHint)
    {
        header.Text = title;
        body.Text = text;
        hint.Text = closeHint;
        var area = Screen.FromPoint(anchor).WorkingArea;
        var size = GetPreferredSize(Size.Empty);
        int x = Math.Clamp(anchor.X + 8, area.Left + 8, Math.Max(area.Left + 8, area.Right - size.Width - 8));
        int y = anchor.Y + 20 + size.Height <= area.Bottom ? anchor.Y + 20 : Math.Max(area.Top + 8, anchor.Y - size.Height - 12);
        Location = new Point(x, y);
        for (int key = 1; key < held.Length; key++) held[key] = Keyboard.IsDown((ushort)key);
        if (!Visible) Show();
        watcher.Start();
    }

    private void CloseOnInput()
    {
        for (int key = 1; key < held.Length; key++)
        {
            bool down = Keyboard.IsDown((ushort)key);
            if (down && !held[key])
            {
                watcher.Stop();
                Hide();
                return;
            }
            held[key] = down;
        }
    }
}
