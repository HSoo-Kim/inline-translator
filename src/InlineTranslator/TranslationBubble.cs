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

internal static class SelectionLocator
{
    private const int TextPatternId = 10014;

    // Screen rectangle around the selected text, from UI Automation's TextPattern. Null when the app
    // does not expose its selection, in which case callers fall back to the caret or mouse position.
    public static Rectangle? Find(nint foreground, Action<Exception>? onError = null, Action<string>? trace = null)
    {
        object? automation = null;
        try
        {
            automation = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))!);
            var uia = (IUIAutomation)automation!;
            uint thread = Native.GetWindowThreadProcessId(foreground, out _);
            var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf<Native.GuiThreadInfo>() };
            nint focus = thread != 0 && Native.GetGUIThreadInfo(thread, ref info) && info.Focus != 0 ? info.Focus : foreground;
            var pattern = TextPatternOf(focus == 0 ? null : uia.ElementFromHandle(focus), trace)
                ?? TextPatternOf(uia.GetFocusedElement(), trace);
            if (pattern is null) return null;
            var ranges = pattern.GetSelection();
            trace?.Invoke("selection ranges: " + ranges.Length);
            var boxes = new List<RectangleF>();
            for (int i = 0; i < ranges.Length; i++)
            {
                double[] values = ranges.GetElement(i).GetBoundingRectangles() ?? [];
                for (int j = 0; j + 3 < values.Length; j += 4)
                    if (values[j + 2] > 0 && values[j + 3] > 0)
                        boxes.Add(new RectangleF((float)values[j], (float)values[j + 1], (float)values[j + 2], (float)values[j + 3]));
            }
            return BubbleLayout.Union(boxes);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or ArgumentException
            or NotImplementedException or UnauthorizedAccessException or TypeLoadException)
        {
            onError?.Invoke(error);
            return null;
        }
        finally
        {
            if (automation is not null) Marshal.ReleaseComObject(automation);
        }
    }

    private static IUIAutomationTextPattern? TextPatternOf(IUIAutomationElement? element, Action<string>? trace)
    {
        if (element is null) return null;
        var pattern = element.GetCurrentPattern(TextPatternId) as IUIAutomationTextPattern;
        trace?.Invoke($"element type={element.GetCurrentPropertyValue(30003)} class={element.GetCurrentPropertyValue(30012)} text={(pattern is null ? "no" : "yes")}");
        return pattern;
    }

    // Only the vtable slots up to the methods called here are declared; unused slots keep their positions.
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        IUIAutomationElement? ElementFromHandle(nint window);
        void ElementFromPoint();
        IUIAutomationElement? GetFocusedElement();
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        [return: MarshalAs(UnmanagedType.Struct)]
        object? GetCurrentPropertyValue(int propertyId);
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        void RangeFromPoint();
        void RangeFromChild();
        IUIAutomationTextRangeArray GetSelection();
    }

    [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        int Length { get; }
        IUIAutomationTextRange GetElement(int index);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        void Clone();
        void Compare();
        void CompareEndpoints();
        void ExpandToEnclosingUnit();
        void FindAttribute();
        void FindText();
        void GetAttributeValue();
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)]
        double[]? GetBoundingRectangles();
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
    private Color border = Ui.Line;

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
        using var pen = new Pen(border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    public void ShowAt(Rectangle target, string title, string text, string closeHint, bool dark)
    {
        border = dark ? Color.FromArgb(55, 55, 60) : Ui.Line;
        BackColor = Controls[0].BackColor = dark ? Color.FromArgb(24, 24, 27) : Color.White;
        body.ForeColor = dark ? Color.White : Color.FromArgb(17, 17, 17);
        header.ForeColor = dark ? Color.FromArgb(190, 190, 198) : Ui.Muted;
        hint.ForeColor = dark ? Color.FromArgb(140, 140, 150) : Ui.Gray;
        Invalidate(true);
        header.Text = title;
        body.Text = text;
        hint.Text = closeHint;
        Location = BubbleLayout.Placement(target, GetPreferredSize(Size.Empty), Screen.FromRectangle(target).WorkingArea, 8);
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
