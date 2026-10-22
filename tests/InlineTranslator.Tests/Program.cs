using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using InlineTranslator;
using Capture = InlineTranslator.Capture;

internal static class Program
{
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), $"InlineTranslator.Tests-{Guid.NewGuid():N}");

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("hotkeys parse, normalize and format", HotkeyRoundtrip),
            ("invalid hotkeys are rejected", InvalidHotkeys),
            ("defaults: Ctrl+Alt+T to English, Claude Haiku then Codex Luna", Defaults),
            ("normalization repairs presets, engines, tone and apps", NormalizeRepairs),
            ("settings roundtrip atomically with Unicode", SettingsRoundtrip),
            ("missing, corrupt and null settings files", SettingsEdgeFiles),
            ("prompts name the target and stay shell-safe for every language", Prompts),
            ("capture uses selection first and whole input only in listed apps", CaptureRules),
            ("translator uses primary, then fallback, and reports both failures", TranslatorFallback),
            ("translator treats empty output as failure and lets cancellation through", TranslatorEdgeCases),
            ("CLI arguments keep sessions unsaved and tools disabled", EngineArguments),
            ("Codex model cache parsing", CodexModels),
            ("model labels resolve to model IDs", ModelLabels),
            ("sign-in status parsing and sign-in error detection", SignInStatus),
            ("app catalog keeps real apps and merges running ones", AppCatalogMerge),
            ("bubble sits right under the selection, or above it near the screen bottom", BubblePlacement),
            ("Codex errors expose only the ERROR message", CodexErrors),
            ("process runner passes stdin and returns exit codes", ProcessRoundtrip),
            ("process runner kills the CLI on timeout", ProcessTimeout),
            ("release parsing accepts only verifiable GitHub packages", ReleaseParsing),
            ("update installer replaces the executable only after a SHA-256 match", UpdateInstall),
            ("every UI string is translated with matching placeholders", UiStrings),
            ("UI language follows the system when not chosen", UiLanguage),
        };

        int failures = 0;
        Directory.CreateDirectory(TestDirectory);
        ErrorLog.FilePath = Path.Combine(TestDirectory, "error.log");
        try
        {
            foreach (var test in tests)
            {
                try
                {
                    test.Run();
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
                }
            }
        }
        finally
        {
            Directory.Delete(TestDirectory, recursive: true);
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void HotkeyRoundtrip()
    {
        Check(Hotkey.TryParse("ctrl+alt+t", out var hotkey), "lowercase parse");
        Equal("Ctrl+Alt+T", hotkey.ToString());
        Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, hotkey.Modifiers);
        Equal(0x54u, hotkey.VirtualKey);
        Check(Hotkey.TryParse(" Win + Shift + F5 ", out var function), "spaces and F-key");
        Equal("Shift+Win+F5", function.ToString());
        Check(Hotkey.TryParse("Alt+Space", out var space) && space.VirtualKey == 0x20, "named key");
        Check(Hotkey.TryParse(hotkey.ToString(), out var again) && again == hotkey, "format roundtrip");
    }

    private static void InvalidHotkeys()
    {
        foreach (string text in new[] { "", "T", "Shift+A", "Ctrl+", "Ctrl+Ctrl+A", "Ctrl+A+B", "Ctrl+Tab", "Ctrl+Alt+Ä", "Hyper+A" })
            Check(!Hotkey.TryParse(text, out _), "should reject: " + text);
        Check(!new Hotkey(HotkeyModifiers.Shift, 0x41).IsValid, "Shift alone is not enough");
    }

    private static void Defaults()
    {
        var settings = new AppSettings().Normalize();
        Equal(3, settings.Presets.Count);
        Equal(new HotkeyPreset(true, "Ctrl+Alt+T", "en"), settings.Presets[0]);
        Check(!settings.Presets[1].Enabled && !settings.Presets[2].Enabled, "only the first preset is on");
        Equal(new EngineChoice(Provider.Claude, "claude-haiku-5-5"), settings.Primary);
        Equal(new EngineChoice(Provider.Codex, "gpt-5.6-luna"), settings.Fallback);
        Check(settings.WholeInputApps.Contains("Telegram"), "Telegram translates the whole input");
        Check(settings.CheckForUpdates && settings.AutoInstallUpdates, "updates on by default");
    }

    private static void NormalizeRepairs()
    {
        var settings = new AppSettings
        {
            UiLanguage = "KO",
            Primary = new EngineChoice(Provider.None, ""),
            Fallback = new EngineChoice(Provider.Claude, "claude-haiku-5-5"),
            Presets =
            [
                new(true, "ctrl+alt+t", "FR"),
                new(true, "Ctrl+Alt+T", "de"),
                new(true, "Shift+X", "xx"),
                new(true, "Ctrl+Alt+Z", "es"),
            ],
            Tone = "angry",
            BubbleTheme = "neon",
            WholeInputApps = ["Telegram.exe", "telegram", " Slack ", "bad|name", ""],
        }.Normalize();

        Equal("ko", settings.UiLanguage);
        Equal(new EngineChoice(Provider.Claude, "claude-haiku-5-5"), settings.Primary);
        Equal(Provider.None, settings.Fallback.Provider);
        Equal(3, settings.Presets.Count);
        Equal(new HotkeyPreset(true, "Ctrl+Alt+T", "fr"), settings.Presets[0]);
        Equal(new HotkeyPreset(false, "Ctrl+Alt+T", "de"), settings.Presets[1]);
        Equal(new HotkeyPreset(false, "", "en"), settings.Presets[2]);
        Equal("natural", settings.Tone);
        Equal("light", settings.BubbleTheme);
        Equal("Telegram,Slack", string.Join(",", settings.WholeInputApps));

        var models = new AppSettings
        {
            Primary = new EngineChoice(Provider.Codex, "gpt 5; rm -rf"),
            Fallback = new EngineChoice(Provider.Claude, " sonnet "),
            Presets = [new(true, "Ctrl+Alt+T", "en")],
        }.Normalize();
        Equal(new EngineChoice(Provider.Codex, "gpt-5.6-luna"), models.Primary);
        Equal(new EngineChoice(Provider.Claude, "sonnet"), models.Fallback);
        Equal(new HotkeyPreset(false, "Ctrl+Alt+J", "ja"), models.Presets[1]);
    }

    private static void SettingsRoundtrip()
    {
        string path = Path.Combine(TestDirectory, "roundtrip", "settings.json");
        var settings = new AppSettings
        {
            Primary = new EngineChoice(Provider.Codex, "gpt-5.6-luna"),
            Fallback = new EngineChoice(Provider.Claude, "claude-haiku-5-5"),
            Presets = [new(true, "Ctrl+Alt+E", "zh-TW"), new(true, "Ctrl+Shift+F9", "ko"), new(false, "", "ar")],
            WholeInputApps = ["카카오톡", "Telegram"],
            Tone = "formal",
            BubbleTheme = "dark",
            CheckForUpdates = false,
        }.Normalize();
        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path) ?? throw new InvalidOperationException("Saved settings were not found.");
        Equal(settings.Primary, loaded.Primary);
        Equal(settings.Fallback, loaded.Fallback);
        Check(settings.Presets.SequenceEqual(loaded.Presets), "presets survive");
        Check(settings.WholeInputApps.SequenceEqual(loaded.WholeInputApps), "Unicode app names survive");
        Equal("formal", loaded.Tone);
        Equal("dark", loaded.BubbleTheme);
        Check(!loaded.CheckForUpdates, "booleans survive");
        Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
        Check(File.ReadAllText(path).Contains("\"Codex\""), "providers are stored as names");
    }

    private static void SettingsEdgeFiles()
    {
        Check(SettingsStore.Load(Path.Combine(TestDirectory, "missing.json")) is null, "missing file means first run");
        string corrupt = Path.Combine(TestDirectory, "corrupt.json");
        File.WriteAllText(corrupt, "{ not json");
        Equal("Ctrl+Alt+T", SettingsStore.Load(corrupt)!.Presets[0].Hotkey);
        Equal("{ not json", File.ReadAllText(corrupt));
        string nulls = Path.Combine(TestDirectory, "nulls.json");
        File.WriteAllText(nulls, """{"Presets":null,"WholeInputApps":null,"Primary":null,"Fallback":null,"Tone":null}""");
        var loaded = SettingsStore.Load(nulls)!;
        Equal(3, loaded.Presets.Count);
        Equal(Provider.Claude, loaded.Primary.Provider);
        Equal(Provider.None, loaded.Fallback.Provider);
        Equal(0, loaded.WholeInputApps.Count);
    }

    private static void Prompts()
    {
        foreach (var language in Languages.All)
            foreach (string tone in AppSettings.Tones)
            {
                string prompt = Prompt.Instructions(language, tone);
                Check(prompt.Contains("into " + language.EnglishName + ".", StringComparison.Ordinal), "target named: " + language.Code);
                Check(prompt.IndexOfAny(['"', '&', '|', '<', '>', '^', '%', '\n']) < 0, "shell-safe: " + language.Code);
            }
        Check(Prompt.Instructions(Languages.All[0], "casual").Contains("casual", StringComparison.Ordinal), "casual tone");
        Check(Prompt.Instructions(Languages.All[0], "formal").Contains("formal", StringComparison.Ordinal), "formal tone");
        Equal(Languages.All.Count, Languages.All.Select(language => language.Code).Distinct().Count());
        Check(Languages.Find("ZH-cn")?.Code == "zh-CN" && Languages.Find("xx") is null, "language lookup");
    }

    private static void CaptureRules()
    {
        string[] apps = ["Telegram", "KakaoTalk"];
        Equal(CaptureMode.Selection, Capture.Decide(true, "WINWORD", apps));
        Equal(CaptureMode.Selection, Capture.Decide(true, "Telegram", apps));
        Equal(CaptureMode.WholeInput, Capture.Decide(false, "telegram", apps));
        Equal(CaptureMode.WholeInput, Capture.Decide(false, "KakaoTalk.exe", apps));
        Equal(CaptureMode.NeedSelection, Capture.Decide(false, "WINWORD", apps));
        Equal(CaptureMode.NeedSelection, Capture.Decide(false, null, apps));

        Check(!Capture.LooksLikeWholeDocument("응! 근데 몇 시에 시작해?\n조금 늦을 것 같아"), "chat message passes");
        Check(Capture.LooksLikeWholeDocument(new string('가', Capture.WholeInputMaxCharacters + 1)), "very long text is a document");
        Check(Capture.LooksLikeWholeDocument(string.Join("\n", Enumerable.Repeat("line", Capture.WholeInputMaxLines + 1))), "many lines is a document");
        Check(!Capture.LooksLikeWholeDocument(string.Join("\n", Enumerable.Repeat("line", Capture.WholeInputMaxLines))), "line limit is inclusive");
        Check(AppCatalog.IsKnownChatApp("telegram.exe") && !AppCatalog.IsKnownChatApp("WINWORD"), "known chat apps");
    }

    private static void TranslatorFallback()
    {
        var calls = new List<string>();
        var settings = new AppSettings().Normalize();
        var english = Languages.Find("en")!;
        var ok = new Translator((engine, _, text, _) =>
        {
            calls.Add(engine.Provider + ":" + text);
            return Task.FromResult(" Hello \n");
        }).TranslateAsync(settings, english, "안녕", CancellationToken.None).GetAwaiter().GetResult();
        Equal("Hello", ok.Text);
        Equal(Provider.Claude, ok.Engine.Provider);
        Check(ok.PrimaryError is null, "no primary error");
        Equal("Claude:안녕", string.Join(";", calls));

        calls.Clear();
        var fallback = new Translator((engine, instructions, text, _) =>
        {
            calls.Add(engine.Provider.ToString());
            Check(instructions.Contains("English", StringComparison.Ordinal), "instructions passed to every engine");
            return engine.Provider == Provider.Claude ? throw new EngineException("login expired") : Task.FromResult("Hi");
        }).TranslateAsync(settings, english, "안녕", CancellationToken.None).GetAwaiter().GetResult();
        Equal("Hi", fallback.Text);
        Equal(Provider.Codex, fallback.Engine.Provider);
        Equal("login expired", fallback.PrimaryError?.Message);
        Equal("Claude,Codex", string.Join(",", calls));

        var both = Throws<TranslationFailedException>(() => new Translator((engine, _, _, _) =>
            throw new EngineException(engine.Provider + " down")).TranslateAsync(settings, english, "x", CancellationToken.None).GetAwaiter().GetResult());
        Equal("Claude down", both.Primary.Message);
        Equal("Codex down", both.Fallback?.Message);

        var single = settings with { Fallback = new EngineChoice(Provider.None, "") };
        var alone = Throws<TranslationFailedException>(() => new Translator((_, _, _, _) =>
            throw new EngineException("down")).TranslateAsync(single, english, "x", CancellationToken.None).GetAwaiter().GetResult());
        Check(alone.Fallback is null, "no fallback attempted");
    }

    private static void TranslatorEdgeCases()
    {
        var settings = new AppSettings().Normalize() with { Fallback = new EngineChoice(Provider.None, "") };
        var empty = Throws<TranslationFailedException>(() => new Translator((_, _, _, _) => Task.FromResult("  \n"))
            .TranslateAsync(settings, Languages.All[0], "x", CancellationToken.None).GetAwaiter().GetResult());
        Check(empty.Primary is InvalidDataException, "empty output is a failure");

        int calls = 0;
        Throws<OperationCanceledException>(() => new Translator((_, _, _, token) =>
        {
            calls++;
            token.ThrowIfCancellationRequested();
            return Task.FromResult("x");
        }).TranslateAsync(new AppSettings().Normalize(), Languages.All[0], "x", new CancellationToken(true)).GetAwaiter().GetResult());
        Equal(1, calls);
    }

    private static void EngineArguments()
    {
        var claude = EngineCli.ClaudeArguments("haiku", "Translate.");
        Check(claude.Contains("--no-session-persistence") && claude.Contains("--safe-mode") && claude.Contains("--strict-mcp-config"), "claude isolation flags");
        int tools = claude.ToList().IndexOf("--tools");
        Equal("", claude[tools + 1]);
        Equal("haiku", claude[claude.ToList().IndexOf("--model") + 1]);

        var codex = EngineCli.CodexArguments("gpt-5.6-luna", @"C:\Temp\work", @"C:\Temp\work\out.txt");
        Check(codex.Contains("--ephemeral") && codex.Contains("--ignore-user-config"), "codex keeps no session and ignores user hooks");
        Equal("read-only", codex[codex.ToList().IndexOf("--sandbox") + 1]);
        Equal("-", codex[^1]);
        Check(EngineCli.CodexInput("Do it.", "안녕").EndsWith("Message:\n안녕", StringComparison.Ordinal), "message appended after instructions");
    }

    private static void CodexModels()
    {
        string json = """
            {"models":[{"slug":"gpt-6.1-sol","display_name":"GPT-6.1-Sol","visibility":"list"},{"slug":"codex-auto-review","visibility":"hide"},
            {"slug":"gpt-5.6-luna"},{"slug":"bad slug","visibility":"list"},{"visibility":"list"},{"slug":"gpt-6.1-sol","visibility":"list"}]}
            """;
        var models = EngineCli.ParseCodexModels(json);
        Equal("gpt-6.1-sol,gpt-5.6-luna", string.Join(",", models.Select(model => model.Id)));
        Equal("GPT-6.1-Sol,gpt-5.6-luna", string.Join(",", models.Select(model => model.Label)));
        Equal(0, EngineCli.ParseCodexModels("{oops").Count);
        Equal(0, EngineCli.ParseCodexModels("""{"models":{}}""").Count);
    }

    private static void ModelLabels()
    {
        Equal("Haiku 5.5,Sonnet 5.5,Opus 5.5,Fable 5", string.Join(",", EngineCli.ClaudeModels));
        Equal("claude-sonnet-5-5", ModelOption.Resolve(EngineCli.ClaudeModels, " sonnet 5.5 "));
        Equal("claude-opus-5-5", ModelOption.Resolve(EngineCli.ClaudeModels, "claude-opus-5-5"));
        Equal("claude-haiku-4-5", ModelOption.Resolve(EngineCli.ClaudeModels, "claude-haiku-4-5"));
        Equal("Claude · Haiku 5.5", new EngineChoice(Provider.Claude, "claude-haiku-5-5").DisplayName);
        Equal("Claude · opus", new EngineChoice(Provider.Claude, "opus").DisplayName);
    }

    private static void SignInStatus()
    {
        Equal(SignIn.SignedIn, EngineCli.ParseClaudeSignIn(new ProcessResult(0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}", "")));
        Equal(SignIn.SignedOut, EngineCli.ParseClaudeSignIn(new ProcessResult(1, "{\"loggedIn\":false}", "")));
        Equal(SignIn.SignedOut, EngineCli.ParseClaudeSignIn(new ProcessResult(1, "not json", "")));
        Equal(SignIn.Unknown, EngineCli.ParseClaudeSignIn(new ProcessResult(0, "not json", "")));
        Equal(SignIn.SignedIn, EngineCli.ParseCodexSignIn(new ProcessResult(0, "Logged in using ChatGPT\n", "")));
        Equal(SignIn.SignedOut, EngineCli.ParseCodexSignIn(new ProcessResult(1, "", "Not logged in\n")));
        Equal(SignIn.SignedOut, EngineCli.ParseCodexSignIn(new ProcessResult(0, "Not logged in", "")));

        Check(EngineCli.IsSignInError(new EngineException("Claude Code: Failed to authenticate: OAuth session expired and could not be refreshed")), "expired OAuth");
        Check(EngineCli.IsSignInError(new EngineException("Codex: 401 Unauthorized")), "401");
        Check(EngineCli.IsSignInError(new EngineException("Codex: Not logged in. Run codex login.")), "not logged in");
        Check(!EngineCli.IsSignInError(new EngineException("Codex CLI was not found. Install it and sign in once.")), "missing CLI is not a sign-in problem");
        Check(!EngineCli.IsSignInError(new EngineException("Codex: The 'gpt-6-luna' model is not supported.")), "model errors are not sign-in problems");
        Check(!EngineCli.IsSignInError(new TimeoutException("login took too long")), "only engine errors count");

        string line = EngineCli.SignInCommandLine(@"C:\Program Files\Claude\claude.exe", Provider.Claude);
        Equal("/d /c \"\"C:\\Program Files\\Claude\\claude.exe\" auth login & echo. & pause\"", line);
        Check(EngineCli.SignInCommandLine("codex.exe", Provider.Codex).Contains("\"codex.exe\" login &", StringComparison.Ordinal), "codex login command");
    }

    private static void AppCatalogMerge()
    {
        Check(AppCatalog.IsUsefulShortcut("Telegram", @"C:\Apps\Telegram\Telegram.exe"), "normal app kept");
        Check(!AppCatalog.IsUsefulShortcut("Uninstall Telegram", @"C:\Apps\Telegram\unins000.exe"), "uninstaller dropped");
        Check(!AppCatalog.IsUsefulShortcut("카카오톡 제거", @"C:\Apps\Kakao\uninstall.exe"), "Korean uninstaller dropped");
        Check(!AppCatalog.IsUsefulShortcut("Manual", @"C:\Apps\manual.pdf"), "documents dropped");

        var merged = AppCatalog.Merge(
            [
                new AppEntry("KakaoTalk", "카카오톡", @"C:\Kakao\KakaoTalk.exe", false),
                new AppEntry("KakaoTalk", "카카오톡 (두 번째 바로가기)", @"C:\Kakao\KakaoTalk.exe", false),
                new AppEntry("Notepad.exe", "메모장", null, false),
            ],
            [
                new AppEntry("Telegram", "Telegram", @"C:\T\Telegram.exe", true),
                new AppEntry("kakaotalk", "채팅", null, true),
            ]);
        Equal("KakaoTalk,Telegram", string.Join(",", merged.Take(2).Select(app => app.ProcessName).Order()));
        Equal("Notepad", merged[2].ProcessName);
        var kakao = merged.Single(app => app.ProcessName == "KakaoTalk");
        Equal("카카오톡", kakao.DisplayName);
        Check(kakao.Running && kakao.ExecutablePath is not null, "installed entry marked running");
        Check(merged.Take(2).All(app => app.Running), "running apps first");
    }

    private static void BubblePlacement()
    {
        var union = BubbleLayout.Union([new System.Drawing.RectangleF(100, 200, 300, 20), new System.Drawing.RectangleF(80, 222, 120, 20)]);
        Equal(System.Drawing.Rectangle.FromLTRB(80, 200, 400, 242), union!.Value);
        Check(BubbleLayout.Union([]) is null, "no boxes means unknown selection");
        var area = new System.Drawing.Rectangle(0, 0, 1920, 1040);
        var size = new System.Drawing.Size(400, 100);
        Equal(new System.Drawing.Point(80, 250), BubbleLayout.Placement(union.Value, size, area, 8));
        var low = System.Drawing.Rectangle.FromLTRB(300, 980, 500, 1000);
        Equal(new System.Drawing.Point(300, 872), BubbleLayout.Placement(low, size, area, 8));
        var right = System.Drawing.Rectangle.FromLTRB(1800, 100, 1900, 120);
        Equal(new System.Drawing.Point(1512, 128), BubbleLayout.Placement(right, size, area, 8));
    }

    private static void CodexErrors()
    {
        string stderr = "user\n안녕하세요 비밀 메시지\nERROR: {\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'gpt-6-luna' model is not supported.\"}}\n";
        Equal("The 'gpt-6-luna' model is not supported.", EngineCli.CodexError(stderr));
        Equal("", EngineCli.CodexError("user\n안녕하세요 비밀 메시지\n"));
    }

    private static void ProcessRoundtrip()
    {
        string sort = Path.Combine(Environment.SystemDirectory, "sort.exe");
        var result = ProcessRunner.RunAsync(sort, [], "beta\r\nalpha\r\n", TestDirectory, TimeSpan.FromSeconds(30), CancellationToken.None)
            .GetAwaiter().GetResult();
        Equal(0, result.ExitCode);
        Equal("alpha\r\nbeta", result.Output.Trim());

        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var failed = ProcessRunner.RunAsync(cmd, ["/d", "/c", "exit 7"], "", TestDirectory, TimeSpan.FromSeconds(30), CancellationToken.None)
            .GetAwaiter().GetResult();
        Equal(7, failed.ExitCode);
    }

    private static void ProcessTimeout()
    {
        string ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        var clock = Stopwatch.StartNew();
        var error = Throws<TimeoutException>(() => ProcessRunner.RunAsync(ping, ["-n", "60", "127.0.0.1"], "", TestDirectory,
            TimeSpan.FromSeconds(1), CancellationToken.None).GetAwaiter().GetResult());
        Check(clock.Elapsed < TimeSpan.FromSeconds(20), "timed out promptly");
        Check(error.Message.Contains("PING", StringComparison.OrdinalIgnoreCase), "timeout names the CLI");
    }

    private static void ReleaseParsing()
    {
        string digest = new('a', 64);
        var release = Parse($$"""
            {"tag_name":"v0.2.0","html_url":"https://github.com/HSoo-Kim/inline-translator/releases/tag/v0.2.0",
             "assets":[{"name":"other.zip"},{"name":"{{UpdateChecker.PackageName}}","browser_download_url":"https://github.com/x/y.zip","digest":"sha256:{{digest}}"}]}
            """)!;
        Equal(new Version(0, 2, 0), release.Version);
        Equal(digest, release.PackageSha256);
        Equal("https://github.com/x/y.zip", release.PackageUrl?.ToString());
        Check(Parse("""{"tag_name":"v0.2.0","html_url":"https://github.com/a","assets":[{"name":"inline-translator-win-x64.zip","browser_download_url":"https://github.com/x"}]}""")!.PackageUrl is null, "no digest means no automatic install");
        Check(Parse("""{"tag_name":"v0.2.0","html_url":"https://evil.example/"}""") is null, "non-GitHub page rejected");
        Check(Parse("""{"tag_name":"latest","html_url":"https://github.com/a"}""") is null, "non-version tag rejected");
        Equal(new Version(0, 1, 5), UpdateChecker.Current);
    }

    private static void UpdateInstall()
    {
        string folder = Directory.CreateDirectory(Path.Combine(TestDirectory, "update")).FullName;
        string exe = Path.Combine(folder, UpdateChecker.ExecutableName);
        File.WriteAllText(exe, "old");
        string zip = Path.Combine(folder, "package.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(UpdateChecker.ExecutableName).Open()))
            writer.Write("new");
        byte[] bytes = File.ReadAllBytes(zip);
        string sha = Convert.ToHexString(SHA256.HashData(bytes));
        Task<Stream> Download(Uri _, CancellationToken __) => Task.FromResult<Stream>(new MemoryStream(bytes));

        var wrong = new ReleaseInfo(new Version(9, 9, 9), "v9.9.9", "https://github.com/a", new Uri("https://github.com/p.zip"), new string('0', 64));
        Throws<InvalidDataException>(() => UpdateInstaller.InstallAsync(wrong, exe, Download, CancellationToken.None).GetAwaiter().GetResult());
        Equal("old", File.ReadAllText(exe));
        Check(!File.Exists(exe + ".download") && !File.Exists(exe + ".new"), "temporary files removed after failure");

        UpdateInstaller.InstallAsync(wrong with { PackageSha256 = sha }, exe, Download, CancellationToken.None).GetAwaiter().GetResult();
        Equal("new", File.ReadAllText(exe));
        Equal("old", File.ReadAllText(exe + ".old"));
        UpdateInstaller.RemovePrevious(exe);
        Check(!File.Exists(exe + ".old"), "previous version removed on next start");
    }

    private static void UiStrings()
    {
        var placeholder = new Regex(@"\{\d\}");
        foreach (string language in AppSettings.UiLanguages)
        {
            var text = new UiText(language);
            foreach (string key in UiText.Keys)
            {
                string value = text.T(key);
                Check(value.Length > 0, $"{language}: {key}");
                Equal(string.Join(",", placeholder.Matches(key).Select(match => match.Value).Order()),
                    string.Join(",", placeholder.Matches(value).Select(match => match.Value).Order()));
            }
        }
    }

    private static void UiLanguage()
    {
        Equal("ko", UiText.Resolve("", CultureInfo.GetCultureInfo("ko-KR")));
        Equal("zh-CN", UiText.Resolve("", CultureInfo.GetCultureInfo("zh-TW")));
        Equal("en", UiText.Resolve("", CultureInfo.GetCultureInfo("fr-FR")));
        Equal("ja", UiText.Resolve("ja", CultureInfo.GetCultureInfo("ko-KR")));
    }

    private static ReleaseInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return UpdateChecker.Parse(document.RootElement);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T expected)
        {
            return expected;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected <{expected}> but was <{actual}>.");
    }
}
