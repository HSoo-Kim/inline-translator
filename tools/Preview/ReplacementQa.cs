using System.Diagnostics;
using System.Runtime.InteropServices;
using InlineTranslator;

// Explicit live-desktop QA. Every SendInput requires this fixture's foreground HWND.
// No Enter key, external chat, account, or user text is used.
internal static class ReplacementQa
{
    internal static async Task VerifyAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        using var form = new Form
        {
            Text = "Inline Translator replacement QA", ClientSize = new Size(680, 210),
            StartPosition = FormStartPosition.CenterScreen,
        };
        var box = new TextBox { Multiline = true, Bounds = new Rectangle(20, 20, 640, 100) };
        var start = new Button { Text = "Start replacement QA", Bounds = new Rectangle(20, 140, 250, 40) };
        form.Controls.AddRange([box, start]);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        start.Click += (_, _) => ready.TrySetResult();
        await Program.ShowAsync(form);
        Console.WriteLine("REPLACEMENT_QA_READY");
        await ready.Task.WaitAsync(TimeSpan.FromMinutes(2));
        Assert(Native.GetForegroundWindow() == form.Handle, "Start QA from its own foreground window.");
        await Program.FocusAsync(box);
        var clipboard = ClipboardSnapshot.Take();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings
        {
            HomeLanguage = "ko", CheckForUpdates = false, BubblePosition = "selection",
            Presets = [new(true, "Ctrl+Alt+F10", "en")],
            ReadingHotkey = "Ctrl+Alt+F11", ReadingTargetLanguage = "en",
            WholeInputApps = [Process.GetCurrentProcess().ProcessName],
        }.Normalize();
        using var app = new TrayApp(Path.Combine(folder, "settings.json"), settings, false, 0,
            _ => Task.FromResult<ReleaseInfo?>(null),
            async (_, _, _, token) =>
            {
                started.TrySetResult();
                return await output.Task.WaitAsync(token);
            });
        try
        {
            const string source = "회의 자료를 보내 주세요.", translated = "Please send the meeting materials.";
            foreach (string scenario in new[] { "whole", "selected", "changed", "reading" })
            {
                box.Text = scenario == "selected" ? "KEEP|" + source + "|TAIL" : source;
                if (scenario == "selected") box.Select(5, source.Length);
                else if (scenario == "reading") box.SelectAll();
                else box.Select(box.TextLength, 0);
                await Program.FocusAsync(box);
                started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                output = new(TaskCreationOptions.RunContinuationsAsynchronously);
                string expected = scenario == "selected" ? "KEEP|" + translated + "|TAIL" : translated;
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler onText = (_, _) =>
                {
                    if (box.Text == expected) changed.TrySetResult();
                };
                box.TextChanged += onText;
                try
                {
                    Chord(form, Native.VkControl, Native.VkMenu, scenario == "reading" ? (ushort)0x7A : (ushort)0x79);
                    await started.Task.WaitAsync(Program.QaTimeout);
                    if (scenario == "changed")
                    {
                        var edit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        EventHandler onEdit = (_, _) => edit.TrySetResult();
                        box.TextChanged += onEdit;
                        try
                        {
                            Assert(Native.GetForegroundWindow() == form.Handle, "Never type into another window.");
                            var keys = new[]
                            {
                                new Native.Input { Type = 1, Data = new Native.InputUnion { Keyboard = new Native.KeyboardInput { ScanCode = 'X', Flags = 4 } } },
                                new Native.Input { Type = 1, Data = new Native.InputUnion { Keyboard = new Native.KeyboardInput { ScanCode = 'X', Flags = 6 } } },
                            };
                            Assert(Native.SendInput(2, keys, Marshal.SizeOf<Native.Input>()) == 2, "Could not inject fixture-only edit.");
                            await edit.Task.WaitAsync(Program.QaTimeout);
                        }
                        finally { box.TextChanged -= onEdit; }
                        expected = box.Text;
                        Assert(expected != source, "Newer input must differ from the translated source.");
                    }
                    output.TrySetResult(translated);
                    await app.CurrentTranslation.WaitAsync(Program.QaTimeout);
                    if (scenario is "whole" or "selected")
                    {
                        await changed.Task.WaitAsync(Program.QaTimeout);
                        Assert(box.Text == expected, scenario + " input must be replaced.");
                    }
                    else
                    {
                        Assert(box.Text == (scenario == "reading" ? source : expected), scenario + " original/new input must survive.");
                        var bubble = Application.OpenForms.OfType<TranslationBubble>().Single();
                        Assert(bubble.Visible, scenario + " must show the translation bubble.");
                        Capture(bubble, Path.Combine(folder, scenario + "-bubble.png"));
                        bubble.Hide();
                    }
                    Capture(form, Path.Combine(folder, scenario + ".png"));
                    Console.WriteLine("PASS replacement/" + scenario);
                }
                finally { box.TextChanged -= onText; }
            }
        }
        finally
        {
            clipboard.Restore();
        }
        Console.WriteLine("PASS replacement QA; fixture disposed; clipboard restored; no messages sent.");
    }

    private static void Chord(Form form, params ushort[] keys)
    {
        Assert(Native.GetForegroundWindow() == form.Handle, "Never send a shortcut into another window.");
        Keyboard.Chord(keys);
    }

    private static void Capture(Form form, string path)
    {
        form.Update();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path);
        Console.WriteLine(path);
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
