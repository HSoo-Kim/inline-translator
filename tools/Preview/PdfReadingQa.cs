using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using InlineTranslator;

internal static class PdfReadingQa
{
    // Live, explicitly selected PDF capture QA. The engine is a local stub:
    // no document text is uploaded and reading must never paste into the PDF.
    internal static async Task VerifyAsync(string handle, string folder, string expectedTitle = "InlineTranslator-reading-fixture.pdf", bool selectAll = false)
    {
        nint pdf = (nint)long.Parse(handle);
        var title = new StringBuilder(512);
        GetWindowText(pdf, title, title.Capacity);
        if (!title.ToString().StartsWith(expectedTitle + " - Adobe Acrobat", StringComparison.Ordinal))
            throw new InvalidOperationException("The explicitly selected PDF tab must be active.");
        Directory.CreateDirectory(folder);
        var previous = Native.GetForegroundWindow();
        bool minimized = IsIconic(pdf);
        if (minimized) ShowWindow(pdf, 9);
        uint thread = GetWindowThreadProcessId(previous, out _);
        bool attached = AttachThreadInput(GetCurrentThreadId(), thread, true);
        try { SetForegroundWindow(pdf); }
        finally { if (attached) AttachThreadInput(GetCurrentThreadId(), thread, false); }
        if (Native.GetForegroundWindow() != pdf) throw new InvalidOperationException("PDF fixture must be foreground.");
        if (selectAll && expectedTitle == "Problem-reading-fixture.pdf")
        {
            Keyboard.Chord(Native.VkControl, Native.VkA);
            Console.WriteLine("PDF_QA_SELECT_ALL_OWNED_COPY");
        }
        var clipboard = ClipboardSnapshot.Take();
        Console.WriteLine("PDF_QA_CLIPBOARD_SAVED");
        var settings = new AppSettings
        {
            CheckForUpdates = false, ReadingHotkey = "Ctrl+Alt+F11", ReadingTargetLanguage = "ko",
            Presets = [new(false, "Ctrl+Alt+F10", "en")], BubblePosition = "selection"
        }.Normalize();
        bool reachedEngine = false;
        var captureClock = Stopwatch.StartNew();
        using var app = new TrayApp(Path.Combine(folder, "settings.json"), settings, false, 0,
            _ => Task.FromResult<ReleaseInfo?>(null), (_, _, input, _) =>
            {
                if (input.Length > 5000)
                    throw new InvalidOperationException("Selection exceeds this local capture QA limit.");
                reachedEngine = true;
                Console.WriteLine("PDF_QA_ENGINE input_chars=" + input.Length + " capture_ms=" + captureClock.ElapsedMilliseconds);
                return Task.FromResult("자료를 보내 주세요.");
            });
        try
        {
            Console.WriteLine("PDF_QA_TRIGGER");
            var translate = typeof(TrayApp).GetMethod("TranslateActiveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var operation = (Task)translate.Invoke(app, [0, true])!;
            await operation.WaitAsync(Program.QaTimeout);
            if (!reachedEngine) throw new InvalidOperationException("Reading capture did not reach the engine.");
            var bubble = Application.OpenForms.OfType<TranslationBubble>().Single();
            if (!bubble.Visible) throw new InvalidOperationException("Reading bubble not visible.");
            using var image = new Bitmap(bubble.Width, bubble.Height);
            bubble.DrawToBitmap(image, new Rectangle(Point.Empty, bubble.Size));
            image.Save(Path.Combine(folder, "pdf-reading-bubble.png"));
            Console.WriteLine("PDF_QA_PASS");
        }
        finally
        {
            clipboard.Restore();
            if (Native.GetForegroundWindow() == pdf)
            {
                if (minimized) ShowWindow(pdf, 6);
                SetForegroundWindow(previous);
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint source, uint target, bool attach);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
}
