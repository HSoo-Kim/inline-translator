using System.Runtime.InteropServices;
using InlineTranslator;

internal static class ShortcutQa
{
    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmLeftDown = 0x0201, WmLeftUp = 0x0202;

    internal static async Task VerifyAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var settings = new AppSettings
        {
            UiLanguage = "en", CheckForUpdates = false,
            Presets = [new(true, "Ctrl+Shift+F23", "en")],
            ReadingHotkeyEnabled = true, ReadingHotkey = "Ctrl+Shift+F24",
        }.Normalize();
        Check(Hotkey.TryParse(settings.Presets[0].Hotkey, out var writing), "Invalid writing QA shortcut.");
        Check(Hotkey.TryParse(settings.ReadingHotkey, out var reading), "Invalid reading QA shortcut.");
        using var probe = new HotkeyWindow(0);
        using var tray = new TrayApp(Path.Combine(folder, "shortcut-settings.json"), settings, false, 0,
            _ => Task.FromResult<ReleaseInfo?>(null));
        try
        {
            Reserved(probe, writing);
            Reserved(probe, reading);
            tray.ShowSettings();
            var form = tray.SettingsWindow!;
            await form.InitialSignIn.WaitAsync(Program.QaTimeout);
            var box = form.Controls.Find("preset-0-hotkey", true).OfType<HotkeyBox>().Single();
            form.ActiveScroller.ScrollControlIntoView(box);
            await Program.FocusAsync(box);
            Reserved(probe, writing);
            Reserved(probe, reading);

            await ArmWithEnterAsync(form, box);
            Available(probe, writing);
            Available(probe, reading);
            await RecordShortcutAsync(box, writing);
            await Program.FocusAsync(form.Controls.Find("cancel", true).OfType<Button>().Single());
            Reserved(probe, writing);
            Reserved(probe, reading);

            await Program.FocusAsync(box);
            Click(box);
            Available(probe, writing);
            Available(probe, reading);
            using (var other = new Form { Text = "Shortcut QA focus", ShowInTaskbar = false })
            {
                await Program.ShowAsync(other);
                Reserved(probe, writing);
                Reserved(probe, reading);
                other.Close();
            }
            tray.ShowSettings();
            Check(ReferenceEquals(form, tray.SettingsWindow), "Activation must reuse Settings.");
            Reserved(probe, writing);
            Reserved(probe, reading);

            await Program.FocusAsync(box);
            Click(box);
            Available(probe, writing);
            form.WindowState = FormWindowState.Minimized;
            Reserved(probe, writing);
            tray.ShowSettings();
            Check(ReferenceEquals(form, tray.SettingsWindow) && form.WindowState == FormWindowState.Normal,
                "Reopening minimized Settings must restore the existing window.");
            Reserved(probe, writing);

            await Program.FocusAsync(box);
            Click(box);
            using (var conflict = new HotkeyWindow(0))
            {
                Check(conflict.Register(43, writing), "A different app must be able to claim a suspended shortcut.");
                await Program.FocusAsync(form.Controls.Find("cancel", true).OfType<Button>().Single());
                Reserved(probe, reading);
                conflict.UnregisterAll();
                Available(probe, writing);
            }

            var pause = tray.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Pause hotkeys");
            pause.PerformClick();
            Check(pause.Checked, "Pause must remain selected.");
            Available(probe, writing);
            await Program.FocusAsync(box);
            Click(box);
            Available(probe, writing);
            await Program.FocusAsync(form.Controls.Find("cancel", true).OfType<Button>().Single());
            Check(pause.Checked, "Ending recording must not clear manual pause.");
            Available(probe, writing);
            pause.PerformClick();
            Reserved(probe, writing);
            Reserved(probe, reading);

            await Program.FocusAsync(box);
            Click(box);
            Available(probe, writing);
            form.Close();
            Check(tray.SettingsWindow is null, "Closing Settings must discard the window.");
            Reserved(probe, writing);
            Reserved(probe, reading);
            tray.ShowSettings();
            Check(tray.SettingsWindow is { IsDisposed: false } && !ReferenceEquals(form, tray.SettingsWindow),
                "Opening Settings after close must create a new window.");
            Reserved(probe, writing);
            Console.WriteLine("PASS shortcut registration, deliberate recording, focus/deactivation/close, pause and Settings restoration. Real physical shortcut delivery is not exercised.");
        }
        finally { tray.ExitThread(); }
    }

    private static async Task RecordShortcutAsync(HotkeyBox box, Hotkey expected)
    {
        box.SetDraft("Ctrl+Shift+F22");
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs e)
        {
            if (box.GetHotkey() == expected) captured.TrySetResult();
        }
        box.TextChanged += Changed;
        var previous = new byte[256];
        Check(GetKeyboardState(previous), "Could not read the private desktop keyboard state.");
        var modifiers = (byte[])previous.Clone();
        modifiers[(int)Keys.ControlKey] |= 0x80;
        modifiers[(int)Keys.ShiftKey] |= 0x80;
        try
        {
            Check(SetKeyboardState(modifiers), "Could not set the private desktop modifier state.");
            Check(Native.PostMessage(box.Handle, WmKeyDown, (nint)Keys.F23, 0), "Could not post the QA shortcut.");
            await captured.Task.WaitAsync(Program.QaTimeout);
        }
        finally
        {
            SetKeyboardState(previous);
            box.TextChanged -= Changed;
        }
        Check(box.GetHotkey() == expected, "Recording must capture the currently registered shortcut.");
    }

    private static async Task ArmWithEnterAsync(SettingsForm form, HotkeyBox box)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(bool recording) { if (recording) started.TrySetResult(); }
        form.HotkeyRecordingChanged += Changed;
        try
        {
            Check(Native.PostMessage(box.Handle, WmKeyDown, (nint)Keys.Enter, 0), "Could not post Enter to QA field.");
            Check(Native.PostMessage(box.Handle, WmKeyUp, (nint)Keys.Enter, 0), "Could not release Enter on QA field.");
            await started.Task.WaitAsync(Program.QaTimeout);
        }
        finally { form.HotkeyRecordingChanged -= Changed; }
    }

    private static void Click(Control box)
    {
        SendMessageW(box.Handle, WmLeftDown, 1, (nint)0x00050005);
        SendMessageW(box.Handle, WmLeftUp, 0, (nint)0x00050005);
    }

    private static void Available(HotkeyWindow probe, Hotkey hotkey)
    {
        Check(probe.Register(42, hotkey), $"{hotkey} must be available to another registration.");
        probe.UnregisterAll();
    }

    private static void Reserved(HotkeyWindow probe, Hotkey hotkey)
    {
        bool available = probe.Register(42, hotkey);
        if (available) probe.UnregisterAll();
        Check(!available, $"{hotkey} must remain registered by the app.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);
}
