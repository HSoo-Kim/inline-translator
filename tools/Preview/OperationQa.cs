using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using InlineTranslator;

internal static class OperationQa
{
    private const uint WmChar = 0x0102;
    private const int Escape = 0x1B, ProbeId = 20, HotkeyAlreadyRegistered = 1409;

    internal static bool RequirePrivateDesktop()
    {
        string? expected = Environment.GetEnvironmentVariable("INLINETRANSLATOR_QA_DESKTOP");
        var name = new StringBuilder(256);
        nint desktop = GetThreadDesktop(GetCurrentThreadId());
        if (expected is not null && expected.StartsWith("InlineTranslator-QA-", StringComparison.Ordinal)
            && desktop != 0
            && GetUserObjectInformationW(desktop, 2, name, (uint)(name.Capacity * sizeof(char)), out _)
            && string.Equals(name.ToString(), expected, StringComparison.Ordinal))
            return true;

        Console.Error.WriteLine("UI QA requires the matching private desktop: PrivateDesktopQa --log <new-file> --timeout 180 -- <dotnet.exe> <Preview.dll> render <folder> | verify");
        return false;
    }

    internal static async Task VerifyAsync()
    {
        using var form = new Form { Text = "Inline Translator private operation QA", ClientSize = new Size(420, 180), ShowInTaskbar = false };
        using var other = new Form { Text = "Inline Translator private focus QA", ClientSize = new Size(260, 120), ShowInTaskbar = false };
        var first = new TextBox { Location = new Point(12, 12), Width = 360 };
        var second = new TextBox { Location = new Point(12, 52), Width = 360, ReadOnly = true };
        form.Controls.AddRange([first, second]);
        await Program.ShowAsync(form);
        await Program.FocusAsync(first);

        var settings = new AppSettings { HomeLanguage = "ko" }.Normalize();
        Assert(settings.Fallback.Provider != Provider.None, "Cancellation QA must have a fallback configured.");
        var calls = new List<EngineChoice>();
        var engineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool block = true;
        async Task<string> RunEngine(EngineChoice engine, string instructions, string text, CancellationToken token)
        {
            calls.Add(engine);
            if (!block) return "recovered";
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = token.Register(() => completion.TrySetCanceled(token));
            engineStarted.TrySetResult();
            return await completion.Task;
        }
        var translator = new Translator(RunEngine);
        var target = Languages.Find("en") ?? throw new InvalidOperationException("English target missing.");
        AssertEscapeAvailable(form.Handle);
        using (var guard = new TranslationGuard(form.Handle))
        {
            AssertEscapeReserved(form.Handle);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = guard.Token.Register(() => cancelled.TrySetResult());
            Task<TranslationResult> operation = translator.TranslateAsync(settings, target, "message", guard.Token);
            await engineStarted.Task.WaitAsync(Program.QaTimeout);
            Assert(!operation.IsCompleted, "Fake engine must be blocked before Esc.");
            await SendCharacterAsync(first, 'x');
            Assert(!operation.IsCompleted && !guard.Token.IsCancellationRequested,
                "Ordinary private WM_CHAR must not cancel the translation.");
            Assert(!guard.InputChanged, "Private WM_CHAR is not a hardware raw-input notification.");

            // Both token subscriptions exist before posting to this guard's own HWND.
            Assert(Native.PostMessage(guard.Handle, Native.WmHotkey, 1, Escape << 16),
                "Could not post Esc WM_HOTKEY to the private guard.");
            await cancelled.Task.WaitAsync(Program.QaTimeout);
            try
            {
                await operation.WaitAsync(Program.QaTimeout);
                throw new InvalidOperationException("Esc did not cancel the blocked Translator operation.");
            }
            catch (OperationCanceledException error) when (guard.Token.IsCancellationRequested)
            {
                Assert(error.CancellationToken == guard.Token, "Cancellation must carry the guard token.");
            }
            Assert(calls.Count == 1 && calls[0] == settings.Primary, "Cancelled primary must not run fallback.");
            Assert(!guard.CanReplace, "Cancelled translation must not replace input.");
            AssertEscapeReserved(form.Handle);
            guard.FinishTranslation();
            AssertEscapeAvailable(form.Handle);
        }
        AssertEscapeAvailable(form.Handle);
        Console.WriteLine("PASS Esc conflict, own-HWND cancellation, blocked engine cancellation without fallback, FinishTranslation release, private WM_CHAR.");

        block = false;
        using (var guard = new TranslationGuard(form.Handle))
        {
            AssertEscapeReserved(form.Handle);
            var result = await translator.TranslateAsync(settings, target, "another message", guard.Token)
                .WaitAsync(Program.QaTimeout);
            Assert(result.Text == "recovered" && result.Engine == settings.Primary && result.PrimaryError is null,
                "Same Translator must translate successfully with a fresh guard after cancellation.");
            Assert(calls.Count == 2 && calls.All(engine => engine == settings.Primary), "Recovery must not use fallback.");
            guard.FinishTranslation();
            AssertEscapeAvailable(form.Handle);
            SendMessageW(guard.Handle, Native.WmHotkey, 1, Escape << 16);
            Assert(!guard.Token.IsCancellationRequested, "Released guard must ignore late Esc messages.");
            await SendCharacterAsync(first, 'y');
        }
        using (var guard = new TranslationGuard(form.Handle))
            AssertEscapeReserved(form.Handle);
        AssertEscapeAvailable(form.Handle);
        Console.WriteLine("PASS fresh-guard recovery, late Esc ignored after finish, Dispose releases Esc.");

        await Program.FocusAsync(first);
        Assert(!FocusInspector.IsReadOnly(form.Handle), "Editable private TextBox must not be read-only.");
        using (var guard = new TranslationGuard(form.Handle))
        {
            bool foregroundBaseline = guard.CanReplace;
            Assert(!guard.InputChanged && !guard.Token.IsCancellationRequested, "New focus guard must be unchanged and uncancelled.");
            await Program.FocusAsync(second);
            Assert(FocusInspector.IsReadOnly(form.Handle), "Read-only private TextBox must be detected.");
            Assert(!guard.CanReplace, "Moving focus to another control must forbid replacement.");
            if (foregroundBaseline)
            {
                guard.CaptureFocus();
                Assert(!guard.CanReplace && guard.InputChanged, "CaptureFocus must not re-arm replacement after focus moved.");
                await Program.FocusAsync(first);
                Assert(!guard.CanReplace, "Focus change after CaptureFocus must forbid replacement.");
                Console.WriteLine("PASS CanReplace control-focus invalidation remains sticky after CaptureFocus.");
            }
            else
            {
                Assert(Native.GetForegroundWindow() != form.Handle,
                    "CanReplace must initially be true when the unchanged private form owns foreground.");
                Console.WriteLine("LIMIT private desktop has no foreground baseline; CanReplace remained false, so true-to-false focus invalidation was not established.");
            }

            guard.CaptureFocus();
            bool beforeOther = guard.CanReplace;
            await Program.ShowAsync(other);
            if (beforeOther && Native.GetForegroundWindow() == other.Handle)
            {
                Assert(!guard.CanReplace, "Another private foreground form must forbid replacement.");
                Console.WriteLine("PASS CanReplace foreground-window invalidation.");
            }
            else
            {
                Console.WriteLine("LIMIT inactive desktop did not establish both private foreground states; foreground-window transition not established.");
            }
        }
        AssertEscapeAvailable(form.Handle);
        Console.WriteLine("PASS operation QA complete. WM_CHAR and WM_HOTKEY were delivered only to QA-owned handles; no physical input or clipboard replacement was exercised.");
    }

    private static async Task SendCharacterAsync(TextBox box, char character)
    {
        string expected = box.Text + character;
        box.Select(box.TextLength, 0);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => changed.TrySetResult();
        box.TextChanged += handler;
        try
        {
            Assert(Native.PostMessage(box.Handle, WmChar, character, 0), "Could not post WM_CHAR to private TextBox.");
            await changed.Task.WaitAsync(Program.QaTimeout);
            Assert(box.Text == expected, "Private TextBox must accept ordinary WM_CHAR during and after translation.");
        }
        finally
        {
            box.TextChanged -= handler;
        }
    }

    private static void AssertEscapeAvailable(nint probe)
    {
        bool registered = Native.RegisterHotKey(probe, ProbeId, Native.ModNoRepeat, Escape);
        if (!registered) throw new Win32Exception(Marshal.GetLastWin32Error(), "Esc was not released.");
        Assert(Native.UnregisterHotKey(probe, ProbeId), "Could not release QA's Esc probe.");
    }

    private static void AssertEscapeReserved(nint probe)
    {
        bool registered = Native.RegisterHotKey(probe, ProbeId, Native.ModNoRepeat, Escape);
        int error = Marshal.GetLastWin32Error();
        if (registered)
            Assert(Native.UnregisterHotKey(probe, ProbeId), "Could not release unexpected QA Esc registration.");
        Assert(!registered && error == HotkeyAlreadyRegistered,
            $"TranslationGuard must own bare Esc (registered={registered}, error={error}).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint GetThreadDesktop(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder information, uint length, out uint needed);
}
