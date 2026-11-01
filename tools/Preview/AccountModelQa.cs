using System.Diagnostics;
using System.Runtime.InteropServices;
using InlineTranslator;

internal static class AccountModelQa
{
    private const uint WmLeftDown = 0x0201, WmLeftUp = 0x0202;

    internal static async Task VerifyAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        string fakeBin = Path.Combine(folder, "fake-cli");
        string codexHome = Path.Combine(folder, "codex-home");
        Directory.CreateDirectory(fakeBin);
        Directory.CreateDirectory(codexHome);
        string claudeMarker = Path.Combine(folder, "claude-login.txt");
        string codexMarker = Path.Combine(folder, "codex-login.txt");
        File.WriteAllText(Path.Combine(fakeBin, "claude.cmd"), $"@echo off\r\n>\"{claudeMarker}\" echo %*\r\n");
        File.WriteAllText(Path.Combine(fakeBin, "codex.cmd"), $"@echo off\r\n>\"{codexMarker}\" echo %*\r\n");
        File.WriteAllText(Path.Combine(codexHome, "models_cache.json"), """
            {"models":[
              {"slug":"gpt-5.6-luna","display_name":"GPT-5.6-Luna","visibility":"list"},
              {"slug":"gpt-6-luna","display_name":"GPT-6-Luna","visibility":"list"}
            ]}
            """);
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        string? oldCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        Environment.SetEnvironmentVariable("PATH", fakeBin + ";" + Environment.SystemDirectory);
        Environment.SetEnvironmentVariable("CODEX_HOME", codexHome);
        try
        {
            var settings = new AppSettings
            {
                UiLanguage = "en", CheckForUpdates = false,
                Primary = new(Provider.Claude, "claude-haiku-5-5"),
                Fallback = new(Provider.Codex, "codex-custom-existing"),
            }.Normalize();
            var saved = new List<AppSettings>();
            var authenticationFailures = new HashSet<Provider>();
            bool expired = true;
            Task<string> Run(EngineChoice engine, string _, string __, CancellationToken ___) =>
                expired && engine.Provider == Provider.Claude
                    ? Task.FromException<string>(new EngineException("OAuth session expired and could not be refreshed"))
                    : Task.FromResult("A successful engine response.");
            using var icon = AppIcon.Load();
            using var form = new SettingsForm(settings, new UiText("en"), icon, null,
                draft => { saved.Add(draft); return []; }, () => Task.CompletedTask, _ => { }, Run,
                authenticationFailures: authenticationFailures,
                checkSignIn: (_, _) => Task.FromResult(SignIn.SignedIn));
            await Program.ShowAsync(form);
            await form.InitialSignIn.WaitAsync(Program.QaTimeout);
            form.SelectTab("engines");
            var claudeAction = Find<Button>(form, "account-Claude-action");
            var codexAction = Find<Button>(form, "account-Codex-action");
            var claudeStatus = Find<StatusPill>(form, "account-Claude-status");
            Check(claudeAction.Visible && claudeAction.Enabled && claudeAction.Text == "Sign in again",
                "A signed-in Claude account must still offer sign-in again.");
            Check(codexAction.Visible && codexAction.Enabled && codexAction.Text == "Sign in again",
                "A signed-in Codex account must still offer sign-in again.");
            claudeAction.PerformClick();
            await WaitUntilAsync(() => File.Exists(claudeMarker), "Claude login command did not run.");
            Check(File.ReadAllText(claudeMarker).Trim() == "auth login", "Claude button must launch its real login command.");
            await WaitUntilAsync(() => codexAction.Enabled, "Account status did not finish refreshing.");
            codexAction.PerformClick();
            await WaitUntilAsync(() => File.Exists(codexMarker), "Codex login command did not run.");
            Check(File.ReadAllText(codexMarker).Trim() == "login", "Codex button must launch its real login command.");
            await WaitUntilAsync(() => claudeAction.Enabled, "Account status did not finish refreshing.");

            Find<Button>(form, "primary-test").PerformClick();
            await form.LastTest.WaitAsync(Program.QaTimeout);
            Check(claudeStatus.Text.Contains("Sign-in needed", StringComparison.Ordinal) && claudeAction.Text == "Sign in",
                "A real authentication error must override a stale signed-in CLI status.");
            Check(Find<StatusPill>(form, "account-Codex-status").Text.Contains("Signed in", StringComparison.Ordinal),
                "A Claude authentication error must not change Codex status.");
            form.ActiveScroller.ScrollControlIntoView(claudeAction);
            Capture(form, Path.Combine(folder, "auth-expired.png"));
            File.Delete(claudeMarker);
            claudeAction.PerformClick();
            await WaitUntilAsync(() => File.Exists(claudeMarker), "Expired account could not start login.");
            Check(File.ReadAllText(claudeMarker).Trim() == "auth login", "Expired account must launch Claude login.");
            Check(claudeStatus.Text.Contains("Sign-in needed", StringComparison.Ordinal),
                "Opening login alone must not claim authentication recovered.");
            expired = false;
            Find<Button>(form, "primary-test").PerformClick();
            await form.LastTest.WaitAsync(Program.QaTimeout);
            Check(claudeStatus.Text.Contains("Signed in", StringComparison.Ordinal) && claudeAction.Text == "Sign in again",
                "A successful engine test must clear the authentication error.");

            var primaryModel = Find<PillDropdown>(form, "primary-model");
            var fallbackModel = Find<PillDropdown>(form, "fallback-model");
            AddCustomModel(form, fallbackModel, "codex-custom-new");
            var primaryMenu = OpenMenu(form, primaryModel);
            var primaryLabels = Labels(primaryMenu);
            Check(primaryLabels.Contains("GPT-6-Luna") && primaryLabels.Contains("Haiku 5.5")
                && primaryLabels.Contains("codex-custom-existing") && primaryLabels.Contains("codex-custom-new"),
                "Primary model menu must include both engines and configured/custom models.");
            Capture(primaryMenu, Path.Combine(folder, "primary-model-menu.png"));
            Choose(primaryMenu, "GPT-6-Luna");
            Check(Find<PillDropdown>(form, "primary-provider").SelectedLabel == "Codex",
                "Choosing GPT in primary must switch the provider to Codex.");
            var fallbackMenu = OpenMenu(form, fallbackModel);
            Check(primaryLabels.SequenceEqual(Labels(fallbackMenu)), "Primary and fallback must expose the same model choices.");
            Choose(fallbackMenu, "Haiku 5.5");
            Check(Find<PillDropdown>(form, "fallback-provider").SelectedLabel == "Claude Code",
                "Choosing Claude in fallback must switch the provider to Claude.");
            Find<Button>(form, "save").PerformClick();
            Check(saved.Count == 1 && saved[0].Primary == new EngineChoice(Provider.Codex, "gpt-6-luna")
                && saved[0].Fallback == new EngineChoice(Provider.Claude, "claude-haiku-5-5"),
                "Saved engine/model pairs must match the visible model selections.");
            Console.WriteLine("PASS account re-login commands, stale authentication status, recovery, shared model menus, custom models, provider switching and saved choices.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("CODEX_HOME", oldCodexHome);
        }
    }

    private static void AddCustomModel(Form form, PillDropdown dropdown, string model)
    {
        var menu = OpenMenu(form, dropdown);
        var item = menu.Items.OfType<ToolStripMenuItem>().Single(entry => entry.Text == "Enter a model name…");
        bool entered = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window != form && window.Modal && window.Text == "Model");
            if (dialog is null) return;
            var field = Descendants(dialog).OfType<TextBox>().Single();
            field.Text = model;
            entered = true;
            timer.Stop();
            ((Button)dialog.AcceptButton!).PerformClick();
        };
        timer.Start();
        item.PerformClick();
        timer.Stop();
        menu.Close();
        Check(entered, "Custom model dialog must accept a model name.");
    }

    private static ContextMenuStrip OpenMenu(Form form, PillDropdown dropdown)
    {
        ((SettingsForm)form).ActiveScroller.ScrollControlIntoView(dropdown);
        SendMessageW(dropdown.Handle, WmLeftDown, 1, (nint)0x00050005);
        SendMessageW(dropdown.Handle, WmLeftUp, 0, (nint)0x00050005);
        ContextMenuStrip? menu = null;
        EnumThreadWindows(GetCurrentThreadId(), (handle, _) =>
        {
            if (Control.FromHandle(handle) is ContextMenuStrip { Visible: true } candidate) menu = candidate;
            return true;
        }, 0);
        return menu ?? throw new InvalidOperationException("Model dropdown menu did not open.");
    }

    private static string[] Labels(ContextMenuStrip menu) => menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text ?? "").ToArray();

    private static void Choose(ContextMenuStrip menu, string label)
    {
        menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == label).PerformClick();
        menu.Close();
    }

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(child => Descendants(child).Prepend(child));

    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > Program.QaTimeout) throw new TimeoutException(message);
            await Task.Delay(20);
        }
    }

    private static void Capture(Control control, string path)
    {
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        image.Save(path);
        Console.WriteLine(path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, WindowCallback callback, nint parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
