using System.ComponentModel;
using System.Runtime.InteropServices;

namespace InlineTranslator;

// Receives input notifications without hooks or suppression. Mouse movement alone is harmless.
// Escape is reserved only while an operation is active, then returned to the foreground app.
internal sealed class TranslationGuard : NativeWindow, IDisposable
{
    private const int WmInput = 0x00FF;
    private readonly CancellationTokenSource cancellation = new();
    private readonly nint foreground;
    private FocusState? focus;
    private bool inputRegistered, escapeRegistered;
    public bool InputChanged { get; private set; }
    public CancellationToken Token => cancellation.Token;

    public TranslationGuard(nint foreground)
    {
        this.foreground = foreground;
        focus = FocusState.Read(foreground);
        CreateHandle(new CreateParams { Caption = "InlineTranslator operation" });
        try
        {
            escapeRegistered = Native.RegisterHotKey(Handle, 1, Native.ModNoRepeat, 0x1B);
            if (!escapeRegistered) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not reserve Esc to cancel translation.");
            inputRegistered = RegisterRawInputDevices(Devices(0x100, Handle), 2, (uint)Marshal.SizeOf<RawInputDevice>());
            if (!inputRegistered) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not watch input changes.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool CanReplace => focus is not null && !InputChanged && !Token.IsCancellationRequested
        && foreground == Native.GetForegroundWindow() && focus == FocusState.Read(foreground);

    public void CaptureFocus()
    {
        var current = FocusState.Read(foreground);
        if (focus?.Focus != current?.Focus || foreground != Native.GetForegroundWindow()) InputChanged = true;
        focus = current;
    }

    public void FinishTranslation()
    {
        if (!escapeRegistered) return;
        Native.UnregisterHotKey(Handle, 1);
        escapeRegistered = false;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.WmHotkey && message.WParam == 1 && escapeRegistered)
            cancellation.Cancel();
        else if (message.Msg == WmInput)
        {
            uint headerSize = (uint)(8 + IntPtr.Size * 2), size = 0;
            if (GetRawInputData(message.LParam, 0x10000003, null, ref size, headerSize) == uint.MaxValue)
                InputChanged = true;
            else
            {
                byte[] data = new byte[size];
                if (GetRawInputData(message.LParam, 0x10000003, data, ref size, headerSize) == uint.MaxValue)
                    InputChanged = true;
                else if (size >= headerSize + 16)
                {
                    uint type = BitConverter.ToUInt32(data, 0);
                    int start = (int)headerSize;
                    ushort flags = BitConverter.ToUInt16(data, start + (type == 0 ? 4 : 2));
                    uint extraInfo = type == 1 ? BitConverter.ToUInt32(data, start + 12) : 0;
                    if (Capture.InputInvalidates(type, flags, extraInfo)) InputChanged = true;
                }
            }
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (inputRegistered)
        {
            RegisterRawInputDevices(Devices(1, 0), 2, (uint)Marshal.SizeOf<RawInputDevice>());
            inputRegistered = false;
        }
        FinishTranslation();
        DestroyHandle();
        cancellation.Dispose();
    }

    private static RawInputDevice[] Devices(uint flags, nint target) =>
        [new() { UsagePage = 1, Usage = 2, Flags = flags, Target = target },
         new() { UsagePage = 1, Usage = 6, Flags = flags, Target = target }];

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage, Usage;
        public uint Flags;
        public nint Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint input, uint command, [Out] byte[]? data, ref uint size, uint headerSize);

    private readonly record struct FocusState(nint Focus, nint Caret, int Left, int Top, int Right, int Bottom)
    {
        public static FocusState? Read(nint window)
        {
            var info = new Native.GuiThreadInfo { Size = Marshal.SizeOf<Native.GuiThreadInfo>() };
            uint thread = Native.GetWindowThreadProcessId(window, out _);
            if (thread == 0 || !Native.GetGUIThreadInfo(thread, ref info) || info.Focus == 0) return null;
            return new FocusState(info.Focus, info.Caret, info.CaretRect.Left, info.CaretRect.Top, info.CaretRect.Right, info.CaretRect.Bottom);
        }
    }
}
