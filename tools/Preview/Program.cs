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
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            string path = Path.Combine(folder, $"settings-{language}.png");
            bitmap.Save(path);
            Console.WriteLine($"{path} {form.Width}x{form.Height}");
            form.Close();
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
