using System.Diagnostics;
using InlineTranslator;

// Developer tool: renders the settings window off-screen and runs real CLI translations,
// without showing windows or sending keystrokes to the desktop.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is ["render", var folder]) return Render(folder);
        if (args is ["lnk", var shortcut])
        {
            try { Console.WriteLine("target: " + AppCatalog.ShortcutTarget(shortcut)); }
            catch (Exception error) { Console.WriteLine(error); }
            return 0;
        }
        if (args is ["apps"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var apps = AppCatalog.Discover();
            Console.WriteLine($"{apps.Count} apps");
            foreach (var app in apps.Take(40)) Console.WriteLine($"{app.ProcessName} | {app.DisplayName} | running={app.Running}");
            return 0;
        }
        if (args is ["translate", var provider, var model, var language, var message]) return Translate(provider, model, language, message);
        Console.Error.WriteLine("usage: Preview render <folder> | Preview translate <Claude|Codex> <model> <language> <text>");
        return 2;
    }

    private static int Render(string folder)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INLINETRANSLATOR_QA_DESKTOP")))
        {
            Console.Error.WriteLine("render must run on a private desktop: PrivateDesktopQa --log <file> --timeout 120 -- dotnet Preview.dll render <folder>");
            return 2;
        }
        Directory.CreateDirectory(folder);
        foreach (string language in AppSettings.UiLanguages)
        {
            var settings = new AppSettings { UiLanguage = language }.Normalize();
            using var icon = AppIcon.Load();
            using var form = new SettingsForm(settings, new UiText(language), icon, null, _ => [], () => Task.CompletedTask, _ => { });
            form.Show();
            var settle = Stopwatch.StartNew();
            while (settle.Elapsed < TimeSpan.FromSeconds(4))
            {
                Application.DoEvents();
                Thread.Sleep(50);
            }
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            string path = Path.Combine(folder, $"settings-{language}.png");
            bitmap.Save(path);
            Console.WriteLine($"{path} {form.Width}x{form.Height}");
            form.Close();
        }
        using (var bubble = new TranslationBubble())
        {
            bubble.ShowNear(new Point(200, 200), "English · Claude · Haiku 5.5",
                "I'll send the meeting materials by 3 PM tomorrow.", new UiText("ko").T("Read-only text · press any key or click to close"));
            Application.DoEvents();
            using var bitmap = new Bitmap(bubble.Width, bubble.Height);
            bubble.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bubble.Size));
            string path = Path.Combine(folder, "..", "..", "artifacts", "bubble-ko.png");
            bitmap.Save(path);
            Console.WriteLine($"{path} {bubble.Width}x{bubble.Height}");
        }
        using (var form = new Form { Text = "focus check", Size = new Size(400, 200) })
        {
            var editable = new TextBox { Text = "editable", Location = new Point(10, 10), Width = 300 };
            var locked = new TextBox { Text = "read only", ReadOnly = true, Location = new Point(10, 50), Width = 300 };
            form.Controls.AddRange([editable, locked]);
            form.Show();
            Application.DoEvents();
            editable.Focus();
            Application.DoEvents();
            Console.WriteLine($"focus editable -> readOnly={FocusInspector.IsReadOnly(form.Handle)}");
            locked.Focus();
            Application.DoEvents();
            Console.WriteLine($"focus read-only -> readOnly={FocusInspector.IsReadOnly(form.Handle)}");
        }
        using (var picker = new AppPicker(new UiText("ko"), ["Telegram", "KakaoTalk"]))
        {
            picker.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(picker.Width, picker.Height);
            picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, picker.Size));
            string path = Path.Combine(folder, "..", "..", "artifacts", "app-picker-ko.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bitmap.Save(path);
            Console.WriteLine($"{path} {picker.Width}x{picker.Height}");
        }
        return 0;
    }

    private static int Translate(string provider, string model, string language, string message)
    {
        var engine = new EngineChoice(Enum.Parse<Provider>(provider), model);
        var settings = new AppSettings { Primary = engine, Fallback = new EngineChoice(Provider.None, "") }.Normalize();
        var target = Languages.Find(language) ?? throw new ArgumentException("Unknown language " + language);
        var clock = Stopwatch.StartNew();
        try
        {
            var result = new Translator(EngineCli.TranslateAsync).TranslateAsync(settings, target, message, CancellationToken.None)
                .GetAwaiter().GetResult();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine($"{engine.DisplayName} {clock.Elapsed.TotalSeconds:0.0}s: {result.Text}");
            return 0;
        }
        catch (TranslationFailedException failure)
        {
            Console.WriteLine($"{engine.DisplayName} FAILED {clock.Elapsed.TotalSeconds:0.0}s: {failure.Message}");
            return 1;
        }
    }
}
