using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace InlineTranslator;

internal sealed class ClipboardSnapshot
{
    private readonly DataObject? data;

    private ClipboardSnapshot(DataObject? data) => this.data = data;

    public static ClipboardSnapshot Take() => Take(Clipboard.GetDataObject());

    internal static ClipboardSnapshot Take(IDataObject? current)
    {
        if (current is null) return new ClipboardSnapshot(null);
        var copy = new DataObject();
        string[] formats = current.GetFormats(autoConvert: false);
        foreach (string format in formats)
        {
            object? value;
            try
            {
                value = current.GetData(format, autoConvert: false);
            }
            catch (Exception error) when (error is ExternalException or NotSupportedException or InvalidOperationException
                or ArgumentException or OutOfMemoryException)
            {
                // Private or delay-rendered formats cannot always be read back; the remaining formats are still restored.
                continue;
            }
            if (value is not null) copy.SetData(format, autoConvert: false, value);
        }
        bool preserved = copy.GetFormats(autoConvert: false).Length > 0;
        if (formats.Length > 0 && !preserved)
            throw new InvalidOperationException("Clipboard contents cannot be preserved. Copy plain text before trying again.");
        return new ClipboardSnapshot(preserved ? copy : null);
    }

    public void Restore()
    {
        if (data is null) Clipboard.Clear();
        else Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 50);
    }
}

internal static class PrivateClipboard
{
    // Keeps translations out of Windows clipboard history (Win+V) and cloud clipboard sync.
    public static void SetText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, autoConvert: false, text);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", autoConvert: false, new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", autoConvert: false, new MemoryStream(new byte[4]));
        data.SetData("CanUploadToCloudClipboard", autoConvert: false, new MemoryStream(new byte[4]));
        Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 50);
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly List<int> registered = [];
    private readonly uint activationMessage;

    public event Action<int>? HotkeyPressed;
    public event Action? ActivationRequested;

    public HotkeyWindow(uint activationMessage)
    {
        this.activationMessage = activationMessage;
        CreateHandle(new CreateParams());
    }

    public bool Register(int id, Hotkey hotkey)
    {
        bool ok = Native.RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | Native.ModNoRepeat, hotkey.VirtualKey);
        if (ok) registered.Add(id);
        return ok;
    }

    public void UnregisterAll()
    {
        foreach (int id in registered) Native.UnregisterHotKey(Handle, id);
        registered.Clear();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.WmHotkey) HotkeyPressed?.Invoke((int)message.WParam);
        else if (activationMessage != 0 && message.Msg == (int)activationMessage) ActivationRequested?.Invoke();
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}

internal sealed class BusyIndicator : Form
{
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExTransparent = 0x20;
    private readonly Label label = new() { AutoSize = true, ForeColor = Color.White, Margin = Padding.Empty };

    public BusyIndicator()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(37, 99, 235);
        Padding = new Padding(12, 7, 12, 7);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Controls.Add(label);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost | WsExTransparent;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;
        _ = Native.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    public void ShowNear(Point anchor, string message)
    {
        label.Text = message;
        var area = Screen.FromPoint(anchor).WorkingArea;
        var size = GetPreferredSize(Size.Empty);
        int x = Math.Clamp(anchor.X + 8, area.Left, Math.Max(area.Left, area.Right - size.Width));
        int y = anchor.Y + 12 + size.Height <= area.Bottom ? anchor.Y + 12 : anchor.Y - size.Height - 28;
        Location = new Point(x, Math.Max(area.Top, y));
        if (!Visible) Show();
    }
}

internal static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InlineTranslator";

    public static string? Executable =>
        Environment.ProcessPath is { } path && !Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? path : null;

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && Executable is { } exe
                && value.Trim('"').Equals(exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Executable is { } exe) key.SetValue(ValueName, $"\"{exe}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal static class AppIcon
{
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("InlineTranslator.AppIcon");
        return stream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream);
    }
}
