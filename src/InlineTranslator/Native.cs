using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InlineTranslator;

internal static class Native
{
    public const int WmHotkey = 0x0312;
    public const uint ModNoRepeat = 0x4000;
    public const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;
    public const ushort VkA = 0x41, VkC = 0x43, VkV = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
}

internal static class Keyboard
{
    private const uint InputKeyboard = 1, KeyUp = 0x2;
    private static readonly ushort[] Modifiers = [Native.VkControl, Native.VkMenu, Native.VkShift, Native.VkLWin, Native.VkRWin];

    public static void Chord(params ushort[] keys)
    {
        var inputs = new Native.Input[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = Key(keys[i], 0);
            inputs[inputs.Length - 1 - i] = Key(keys[i], KeyUp);
        }
        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.Input>());
        if (sent != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked the simulated keystrokes.");
    }

    public static bool AnyModifierDown() => Modifiers.Any(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0);

    public static bool IsDown(ushort key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    public static async Task WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken token = default)
    {
        var clock = Stopwatch.StartNew();
        while (AnyModifierDown() && clock.Elapsed < timeout) await Task.Delay(15, token);
        token.ThrowIfCancellationRequested();
        if (AnyModifierDown()) throw new TimeoutException("Release the shortcut keys before translating.");
    }

    private static Native.Input Key(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new Native.InputUnion { Keyboard = new Native.KeyboardInput { VirtualKey = virtualKey, Flags = flags } },
    };
}

internal static class ForegroundWindow
{
    public static string? ProcessName(nint window)
    {
        Native.GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    public static Point CaretOrCursor(nint window)
    {
        uint thread = Native.GetWindowThreadProcessId(window, out _);
        var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf<Native.GuiThreadInfo>() };
        if (thread != 0 && Native.GetGUIThreadInfo(thread, ref info) && info.Caret != 0)
        {
            var point = new Point(info.CaretRect.Left, info.CaretRect.Bottom);
            if (Native.ClientToScreen(info.Caret, ref point)) return point;
        }
        return Cursor.Position;
    }
}
