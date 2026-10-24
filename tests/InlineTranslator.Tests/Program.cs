using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using InlineTranslator;
using Capture = InlineTranslator.Capture;

internal static class Program
{
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), $"InlineTranslator.Tests-{Guid.NewGuid():N}");

    private static int Main(string[] args)
    {
        if (args is ["cancellation-child", var pipeName])
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(10000);
            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client);
            writer.WriteLine(Environment.ProcessId);
            reader.ReadLine();
            return 0;
        }
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
            ("reading hotkey normalizes and persists without conflicting with presets", ReadingHotkey),
            ("input protection rejects changed or missing selections and meaningful input", InputProtection),
            ("cancellation stops primary and fallback without delivering a result", TranslationCancellation),
            ("translator uses primary, then fallback, and reports both failures", TranslatorFallback),
            ("translator treats empty output as failure and lets cancellation through", TranslatorEdgeCases),
            ("CLI arguments keep sessions unsaved and tools disabled", EngineArguments),
            ("Codex model cache parsing", CodexModels),
            ("model labels resolve to model IDs", ModelLabels),
            ("sign-in status parsing and sign-in error detection", SignInStatus),
            ("app catalog keeps real apps and merges running ones", AppCatalogMerge),
            ("bubble sits right under the selection, or above it near the screen bottom", BubblePlacement),
            ("my language goes out, everything else comes home", TwoWayDirection),
            ("Codex errors expose only the ERROR message", CodexErrors),
            ("process runner passes stdin and returns exit codes", ProcessRoundtrip),
            ("process runner kills the CLI on timeout", ProcessTimeout),
            ("process runner cancels a running CLI and waits for its exit", ProcessCancellation),
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
        Equal("mouse", settings.BubblePosition);
        Equal("light", settings.BubbleTheme);
        Equal("", settings.HomeLanguage);
        Equal("Ctrl+Alt+R", settings.ReadingHotkey);
        Check(settings.ReadingHotkeyEnabled, "reading shortcut enabled");
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
            BubblePosition = "orbit",
            HomeLanguage = "xx",
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
        Equal("mouse", settings.BubblePosition);
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
            BubblePosition = "selection",
            HomeLanguage = "ja",
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
        Equal("selection", loaded.BubblePosition);
        Equal("ja", loaded.HomeLanguage);
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
                var withHome = Prompt.Instructions(language, tone, Languages.Find(language.Code == "ko" ? "en" : "ko"));
                Check(withHome.IndexOfAny(['"', '&', '|', '<', '>', '^', '%', '\n']) < 0, "two-way shell-safe: " + language.Code);
                Check(prompt.IndexOfAny(['"', '&', '|', '<', '>', '^', '%', '\n']) < 0, "shell-safe: " + language.Code);
            }
        Check(Prompt.Instructions(Languages.All[0], "casual").Contains("casual", StringComparison.Ordinal), "casual tone");
        var twoWay = Prompt.Instructions(Languages.Find("en")!, "natural", Languages.Find("ko"));
        Check(twoWay.Contains("If it is Korean, translate it into English.", StringComparison.Ordinal), "my language goes to the hotkey language");
        Check(twoWay.Contains("such as English or Japanese, translate it into Korean.", StringComparison.Ordinal), "everything else comes to my language");
        Check(Prompt.Instructions(Languages.Find("en")!, "natural", Languages.Find("en")).Contains("return it unchanged", StringComparison.Ordinal), "same home and target keeps text");
        Check(twoWay.IndexOfAny(['"', '&', '|', '<', '>', '^', '%', '\n']) < 0, "two-way prompt stays shell-safe");
        Equal("ko", Languages.FromCulture(CultureInfo.GetCultureInfo("ko-KR")).Code);
        Equal("zh-TW", Languages.FromCulture(CultureInfo.GetCultureInfo("zh-TW")).Code);
        Equal("zh-CN", Languages.FromCulture(CultureInfo.GetCultureInfo("zh-CN")).Code);
        Equal("en", Languages.FromCulture(CultureInfo.GetCultureInfo("sw-KE")).Code);
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

        Check(Capture.PasteWasIgnored("안녕하세요\r\n반가워요", "안녕하세요\n반가워요 "), "same selection after paste means read-only");
        Check(!Capture.PasteWasIgnored("안녕하세요", null), "nothing selected after paste means it worked");
        Check(!Capture.PasteWasIgnored("안녕하세요", "Hello"), "different text means the paste replaced it");
        Check(!Capture.PasteWasIgnored("안녕하세요", ""), "empty copy means nothing is selected");
    }

    private static void TranslatorFallback()
    {
        var calls = new List<string>();
        var settings = new AppSettings { HomeLanguage = "ko" }.Normalize();
        var english = Languages.Find("en")!;
        var ok = new Translator((engine, _, text, _) =>
        {
            calls.Add(engine.Provider + ":" + text);
            return Task.FromResult(" Hello \n");
        }).TranslateAsync(settings, english, "안녕", CancellationToken.None).GetAwaiter().GetResult();
        Equal("Hello", ok.Text);
        Equal(Provider.Claude, ok.Engine.Provider);
        Check(ok.PrimaryError is null, "no primary error");
        Equal("Claude:Translate this message into English.\nBEGIN MESSAGE\n안녕\nEND MESSAGE", string.Join(";", calls));

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
        Equal(0, calls);
    }

    private static void ReadingHotkey()
    {
        var duplicate = new AppSettings { ReadingHotkey = "ctrl+alt+t" }.Normalize();
        Check(!duplicate.ReadingHotkeyEnabled && duplicate.Presets[0].Enabled, "regular preset wins duplicate");
        var invalid = new AppSettings { ReadingHotkey = "Esc" }.Normalize();
        Check(!invalid.ReadingHotkeyEnabled && invalid.ReadingHotkey == "", "bare Esc cannot be a reading shortcut");
        var disabledPreset = new AppSettings { ReadingHotkey = "ctrl+alt+j" }.Normalize();
        Check(disabledPreset.ReadingHotkeyEnabled, "disabled preset does not claim its key");
        Equal("Ctrl+Alt+J", disabledPreset.ReadingHotkey);
        string path = Path.Combine(TestDirectory, "reading.json");
        SettingsStore.Save(path, disabledPreset with { ReadingHotkeyEnabled = false });
        var loaded = SettingsStore.Load(path)!;
        Equal("Ctrl+Alt+J", loaded.ReadingHotkey);
        Check(!loaded.ReadingHotkeyEnabled, "disabled choice survives saving");
        File.WriteAllText(path, "{}");
        Check(SettingsStore.Load(path)!.ReadingHotkeyEnabled, "old settings receive reading default");
        Equal(CaptureMode.NeedSelection, Capture.Decide(false, "Telegram", AppSettings.DefaultWholeInputApps, reading: true));
        Equal(CaptureMode.Selection, Capture.Decide(true, "Telegram", AppSettings.DefaultWholeInputApps, reading: true));
        foreach (string original in new[] { "Hello", "안녕", "こんにちは" })
        {
            var home = Languages.Find("ko")!;
            var result = new Translator((_, _, _, _) => Task.FromResult("안녕"))
                .TranslateAsync(new AppSettings { HomeLanguage = "ko" }, home, original, CancellationToken.None)
                .GetAwaiter().GetResult();
            Equal(home, result.Output);
        }
    }

    private static void InputProtection()
    {
        Check(Capture.SelectionUnchanged("one\r\ntwo", "one\ntwo"), "clipboard newline conversion is harmless");
        Check(!Capture.SelectionUnchanged("old", "new"), "edited text rejected");
        Check(!Capture.SelectionUnchanged("old", null), "lost selection rejected");
        Check(!Capture.SelectionUnchanged("old ", "old"), "whitespace changes must not be ignored");
        Check(Capture.InputInvalidates(1, 0), "key down invalidates replacement");
        Check(!Capture.InputInvalidates(1, 1), "releasing the triggering shortcut is harmless");
        Check(!Capture.InputInvalidates(0, 0), "pointer movement does not invalidate");
        Check(Capture.InputInvalidates(0, 1), "mouse click invalidates");
        Check(Capture.InputInvalidates(0, 0x400), "scroll invalidates");
    }

    private static void TranslationCancellation() => CheckCancellationAsync().GetAwaiter().GetResult();

    private static async Task CheckCancellationAsync()
    {
        foreach (bool fallback in new[] { false, true })
        {
            using var cancel = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var translator = new Translator(async (engine, _, _, token) =>
            {
                calls++;
                if (fallback && engine.Provider == Provider.Claude) throw new EngineException("offline");
                var stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var subscription = token.Register(() => stopped.TrySetCanceled(token));
                started.SetResult();
                return await stopped.Task;
            });
            Task<TranslationResult> pending = translator.TranslateAsync(new AppSettings { HomeLanguage = "ko" },
                Languages.Find("en")!, "안녕", cancel.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancel.Cancel();
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
                throw new InvalidOperationException("Cancelled translation returned a result.");
            }
            catch (OperationCanceledException) { }
            Equal(fallback ? 2 : 1, calls);
        }
        using var late = new CancellationTokenSource();
        var ignoresToken = new Translator((_, _, _, _) =>
        {
            late.Cancel();
            return Task.FromResult("stale result");
        });
        Throws<OperationCanceledException>(() => ignoresToken.TranslateAsync(new AppSettings(),
            Languages.Find("en")!, "안녕", late.Token).GetAwaiter().GetResult());
        using var failedAfterCancel = new CancellationTokenSource();
        var failsLate = new Translator((_, _, _, _) =>
        {
            failedAfterCancel.Cancel();
            throw new EngineException("process ended while cancelling");
        });
        Throws<OperationCanceledException>(() => failsLate.TranslateAsync(
            new AppSettings { Fallback = new EngineChoice(Provider.None, "") },
            Languages.Find("en")!, "안녕", failedAfterCancel.Token).GetAwaiter().GetResult());
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
        Check(EngineCli.CodexInput("Do it.", "안녕").EndsWith("\n\n안녕", StringComparison.Ordinal), "message appended after instructions");
        Equal("Hello\nthere", Translator.Unwrap("BEGIN MESSAGE\r\nHello\nthere\nEND MESSAGE\n"));
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

    private static void TwoWayDirection()
    {
        var en = Languages.Find("en")!; var ko = Languages.Find("ko")!; var ja = Languages.Find("ja")!; var es = Languages.Find("es")!;
        Equal(en, Direction.Resolve("내일 전까지 첨부한 슬라이드 검토 부탁드려요.", en, ko));
        Equal(ko, Direction.Resolve("Please review the attached slides.", en, ko));
        Equal(ko, Direction.Resolve("明日までに添付のスライドを確認してください。", en, ko));
        Equal(ko, Direction.Resolve("Por favor revisa las diapositivas.", en, ko));
        Equal(en, Direction.Resolve("Thanks for today! 내일 회의 자료는 오후 3시까지 보내드릴게요.", en, ko));
        Equal(ko, Direction.Resolve("请明天之前查看幻灯片。", en, ko));
        Equal(en, Direction.Resolve("明日までに確認", en, ja));
        Equal(ja, Direction.Resolve("请明天之前查看幻灯片。", en, ja));
        Equal(en, Direction.Resolve("안녕하세요", es, en));
        Check(Direction.Resolve("Hola, ¿cómo estás?", es, en) is null, "Latin text with a Latin home language is left to the model");
        Equal(en, Direction.Resolve("12345 !!!", en, ko));
        Equal(ko, Direction.Resolve("Hi", ko, ko));
        Equal(Script.Kana, Direction.Dominant("東京駅で待ち合わせしましょう"));
        Equal(Script.Han, Direction.Dominant("东京站见面"));
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

    private static void ProcessCancellation() => ProcessCancellationAsync().GetAwaiter().GetResult();

    private static async Task ProcessCancellationAsync()
    {
        string name = "InlineTranslator-cancel-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var cancel = new CancellationTokenSource();
        Task connected = pipe.WaitForConnectionAsync();
        string exe = Environment.ProcessPath!;
        string[] args = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [typeof(Program).Assembly.Location, "cancellation-child", name] : ["cancellation-child", name];
        Task<ProcessResult> run = ProcessRunner.RunAsync(exe, args, "", TestDirectory, TimeSpan.FromSeconds(30), cancel.Token);
        try
        {
            await connected.WaitAsync(TimeSpan.FromSeconds(15));
            using var reader = new StreamReader(pipe, leaveOpen: true);
            int pid = int.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
            using var child = Process.GetProcessById(pid);
            cancel.Cancel();
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(10));
                throw new InvalidOperationException("Cancelled CLI returned a result.");
            }
            catch (OperationCanceledException) { }
            Check(child.HasExited, "cancellation completes only after the process exits");
        }
        finally
        {
            cancel.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
        }
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
        Equal(new Version(0, 1, 9), UpdateChecker.Current);
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
