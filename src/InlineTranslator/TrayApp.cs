using System.Diagnostics;

namespace InlineTranslator;

internal sealed class TrayApp : ApplicationContext
{
    internal const int ReadingHotkeyId = AppSettings.PresetCount + 1;
    private static readonly TimeSpan SelectionProbe = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan CopyWait = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(400);

    private readonly string settingsPath;
    private readonly UiText text;
    private readonly Icon icon = AppIcon.Load();
    private readonly NotifyIcon tray;
    private readonly HotkeyWindow hotkeys;
    private readonly BusyIndicator indicator = new();
    private readonly TranslationBubble bubble = new();
    private readonly Translator translator;
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = UpdateChecker.CheckIntervalMilliseconds };
    private readonly Func<CancellationToken, Task<ReleaseInfo?>> fetchUpdate;
    private readonly ToolStripMenuItem updateItem = new() { Visible = false };
    private readonly ToolStripMenuItem checkUpdatesItem = new() { Name = "check-updates" };
    private readonly ToolStripMenuItem settingsItem = new();
    private readonly ToolStripMenuItem pauseItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem languageItem = new();
    private readonly ToolStripMenuItem exitItem = new();
    private readonly ToolStripLabel titleItem = new() { Enabled = false };
    private AppSettings settings;
    private SettingsForm? settingsForm;
    private ReleaseInfo? update;
    private bool busy, installing, checkingUpdates;
    internal Task CurrentUpdateCheck { get; private set; } = Task.CompletedTask;
    internal Task CurrentTranslation { get; private set; } = Task.CompletedTask;
    internal ContextMenuStrip TrayMenu => tray.ContextMenuStrip!;
    internal SettingsForm? SettingsWindow => settingsForm;
    private Action? balloonAction;
    private readonly HashSet<Provider> signInReminded = [];

    public TrayApp(string settingsPath, AppSettings settings, bool firstRun, uint activationMessage,
        Func<CancellationToken, Task<ReleaseInfo?>>? fetchUpdate = null, EngineRun? runEngine = null)
    {
        this.settingsPath = settingsPath;
        this.settings = settings;
        this.fetchUpdate = fetchUpdate ?? UpdateChecker.FetchLatestAsync;
        translator = new Translator(runEngine ?? EngineCli.TranslateAsync);
        text = new UiText(settings.UiLanguage);
        hotkeys = new HotkeyWindow(activationMessage);
        hotkeys.HotkeyPressed += id => CurrentTranslation = TranslateActiveAsync(id - 1, reading: id == ReadingHotkeyId);
        hotkeys.ActivationRequested += ShowSettings;

        var menu = new ContextMenuStrip { Renderer = new RoundMenuRenderer(), Font = Ui.Regular() };
        menu.Opened += (_, _) => Ui.RoundWindow(menu);
        menu.Opening += (_, _) => _ = RequestUpdateCheck();
        menu.Items.AddRange([titleItem, new ToolStripSeparator(), updateItem, checkUpdatesItem, settingsItem, languageItem, pauseItem, new ToolStripSeparator(), exitItem]);
        foreach (var (code, name) in SettingsForm.DisplayLanguages)
        {
            var item = new ToolStripMenuItem(name) { Tag = code };
            item.Click += (_, _) => ChangeDisplayLanguage(code);
            languageItem.DropDownItems.Add(item);
        }
        updateItem.Click += async (_, _) => await InstallUpdateAsync();
        checkUpdatesItem.Click += async (_, _) => await RequestUpdateCheck(manual: true);
        settingsItem.Click += (_, _) => ShowSettings();
        pauseItem.CheckedChanged += (_, _) => ApplyHotkeys(notify: true);
        exitItem.Click += (_, _) => ExitThread();
        tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        tray.BalloonTipClicked += (_, _) =>
        {
            var action = balloonAction;
            balloonAction = null;
            action?.Invoke();
        };

        RefreshTexts();
        ApplyHotkeys(notify: true);
        updateTimer.Tick += async (_, _) => await RequestUpdateCheck();
        updateTimer.Start();
        _ = RequestUpdateCheck();
        if (firstRun)
        {
            Notify(text.T("Inline Translator is running in the notification area. Select text and press {0}.", FirstHotkey()), ToolTipIcon.Info);
            ShowSettings();
        }
    }

    private string FirstHotkey() => settings.Presets.FirstOrDefault(preset => preset.Enabled)?.Hotkey ?? "";

    private void RefreshTexts()
    {
        titleItem.Text = $"Inline Translator {UpdateChecker.Current.ToString(3)}";
        settingsItem.Text = text.T("Settings…");
        checkUpdatesItem.Text = text.T(checkingUpdates ? "Checking for updates…" : "Check for updates");
        pauseItem.Text = text.T("Pause hotkeys");
        exitItem.Text = text.T("Exit");
        languageItem.Text = SettingsForm.LanguageLabel(text);
        foreach (ToolStripMenuItem item in languageItem.DropDownItems)
        {
            item.Checked = (string)item.Tag! == settings.UiLanguage;
            if ((string)item.Tag! == "") item.Text = text.T("System default");
        }
        if (update is not null) updateItem.Text = text.T("Update to {0}", update.Tag);
        var active = settings.Presets.Where(preset => preset.Enabled)
            .Select(preset => $"{preset.Hotkey} → {Languages.Find(preset.TargetLanguage)?.NativeName}");
        string tooltip = "Inline Translator" + (pauseItem.Checked ? " · " + text.T("Paused") : "\n" + string.Join("\n", active));
        tray.Text = tooltip.Length <= 127 ? tooltip : tooltip[..127];
    }

    public IReadOnlyList<string> ApplyHotkeys(bool notify)
    {
        hotkeys.UnregisterAll();
        var failed = new List<string>();
        if (!pauseItem.Checked)
        {
            for (int i = 0; i < settings.Presets.Count; i++)
            {
                var preset = settings.Presets[i];
                if (preset.Enabled && Hotkey.TryParse(preset.Hotkey, out var hotkey) && !hotkeys.Register(i + 1, hotkey))
                    failed.Add(preset.Hotkey);
            }
            if (settings.ReadingHotkeyEnabled && Hotkey.TryParse(settings.ReadingHotkey, out var readingHotkey)
                && !hotkeys.Register(ReadingHotkeyId, readingHotkey))
                failed.Add(settings.ReadingHotkey);
        }
        RefreshTexts();
        if (notify && failed.Count > 0)
            Notify(text.T("{0} is already used by another program. Choose another hotkey in Settings.", string.Join(", ", failed)), ToolTipIcon.Warning);
        return failed;
    }

    internal void ShowSettings()
    {
        if (settingsForm is { IsDisposed: false })
        {
            settingsForm.Activate();
            _ = RequestUpdateCheck();
            return;
        }
        settingsForm = new SettingsForm(settings, text, icon, update, SaveSettings, InstallUpdateAsync, ChangeDisplayLanguage,
            checkUpdates: () => RequestUpdateCheck(manual: true));
        settingsForm.FormClosed += (_, _) => settingsForm = null;
        settingsForm.Show();
        settingsForm.Activate();
        _ = RequestUpdateCheck();
    }

    private void ChangeDisplayLanguage(string code)
    {
        bool reopen = settingsForm is { IsDisposed: false };
        settingsForm?.Close();
        try
        {
            SaveSettings(settings with { UiLanguage = code });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Could not save settings", error);
            Notify(text.T("Could not save settings: {0}", error.Message), ToolTipIcon.Error);
        }
        if (reopen) ShowSettings();
    }

    private IReadOnlyList<string> SaveSettings(AppSettings updated)
    {
        settings = updated.Normalize();
        SettingsStore.Save(settingsPath, settings);
        text.SetLanguage(settings.UiLanguage);
        if (settings.CheckForUpdates) _ = RequestUpdateCheck();
        return ApplyHotkeys(notify: false);
    }

    private async Task TranslateActiveAsync(int presetIndex, bool reading = false)
    {
        if (busy || installing) return;
        if (reading ? !settings.ReadingHotkeyEnabled : presetIndex < 0 || presetIndex >= settings.Presets.Count) return;
        var preset = reading ? null : settings.Presets[presetIndex];
        var target = Languages.Find(reading ? settings.ReadingTargetLanguage : preset!.TargetLanguage);
        if ((!reading && !preset!.Enabled) || target is null) return;
        busy = true;
        ClipboardSnapshot? snapshot = null;
        bool restoreClipboard = false;
        uint ownedClipboard = 0;
        TranslationGuard? guard = null;
        try
        {
            nint window = Native.GetForegroundWindow();
            // Do not observe the triggering shortcut's queued key presses as edits.
            await Keyboard.WaitForModifiersReleasedAsync(TimeSpan.FromSeconds(2));
            if (Native.GetForegroundWindow() != window) return;
            guard = new TranslationGuard(window);
            var token = guard.Token;
            async Task<string?> CopySelectionAsync(bool selectAll, TimeSpan wait)
            {
                uint before = Native.GetClipboardSequenceNumber();
                try { return await CopyAsync(selectAll, wait, token); }
                finally
                {
                    uint after = Native.GetClipboardSequenceNumber();
                    if (after != before && !guard.InputChanged)
                    {
                        ownedClipboard = after;
                        restoreClipboard = true;
                    }
                }
            }
            snapshot = ClipboardSnapshot.Take();
            string? copied = await CopySelectionAsync(selectAll: false, SelectionProbe);
            string? process = ForegroundWindow.ProcessName(window);
            var mode = Capture.Decide(!string.IsNullOrWhiteSpace(copied), process, settings.WholeInputApps, reading);
            if (mode == CaptureMode.WholeInput)
            {
                if (!guard.CanReplace) return;
                copied = await CopySelectionAsync(selectAll: true, CopyWait);
                if (string.IsNullOrWhiteSpace(copied))
                {
                    Notify(text.T("Could not read the input box in {0}. Drag-select the text, then press the hotkey.", process ?? ""), ToolTipIcon.Info);
                    return;
                }
                if (Capture.LooksLikeWholeDocument(copied))
                {
                    Notify(text.T("That looks like a whole document, not a chat message, so nothing was translated. Drag-select the part to translate, or remove {0} from the whole-input apps in Settings.", process ?? ""), ToolTipIcon.Warning);
                    return;
                }
            }
            if (mode == CaptureMode.NeedSelection || string.IsNullOrWhiteSpace(copied))
            {
                Notify(text.T("Select the text to translate first, then press the hotkey."), ToolTipIcon.Info);
                return;
            }

            guard.CaptureFocus();
            bool readOnly = reading || mode == CaptureMode.Selection && FocusInspector.IsReadOnly(window);
            var mouse = Cursor.Position;
            Rectangle? selection = settings.BubblePosition == "selection" && mode == CaptureMode.Selection ? SelectionLocator.Find(window) : null;
            var near = selection ?? new Rectangle(mouse.X + 12, mouse.Y, 1, 12);
            var heading = Direction.Resolve(copied, target, Languages.Home(settings.HomeLanguage)) ?? target;
            if (restoreClipboard && Native.GetClipboardSequenceNumber() == ownedClipboard) RestoreClipboard(snapshot);
            restoreClipboard = false;
            indicator.ShowNear(new Point(near.Left, near.Bottom), text.T("Translating to {0}… · Esc to cancel", heading.NativeName));
            var result = await translator.TranslateAsync(settings, target, copied, token);
            token.ThrowIfCancellationRequested();
            indicator.Hide();
            if (result.PrimaryError is { } primaryError)
            {
                ErrorLog.Write($"{settings.Primary.DisplayName} failed, used {result.Engine.DisplayName}", primaryError);
                if (EngineCli.IsSignInError(primaryError) && signInReminded.Add(settings.Primary.Provider))
                    NotifySignIn(settings.Primary.Provider, text.T("{0} needs you to sign in again, so {1} translated this. Click here to sign in to {0}.",
                        EngineCli.ProductName(settings.Primary.Provider), EngineCli.ProductName(result.Engine.Provider)));
            }

            void ShowBubble(bool changed = false) => bubble.ShowAt(near, $"→ {(result.Output ?? target).NativeName} · {result.Engine.DisplayName}", result.Text,
                text.T(changed ? "Input changed · translation shown without replacing text" : "Translation · press any key or click to close"), settings.BubbleTheme == "dark",
                followMouse: settings.BubblePosition == "mouse");

            if (readOnly)
            {
                ShowBubble();
                return;
            }

            if (!guard.CanReplace)
            {
                ShowBubble(changed: true);
                return;
            }

            // Verify the original selection, including whole-input selections. Never select all again:
            // doing so could overwrite text typed while the engine was working.
            snapshot = ClipboardSnapshot.Take();
            string? current = await CopySelectionAsync(selectAll: false, SelectionProbe);
            if (!guard.CanReplace || !Capture.SelectionUnchanged(copied, current))
            {
                ShowBubble(changed: true);
                return;
            }
            token.ThrowIfCancellationRequested();
            guard.FinishTranslation();
            PrivateClipboard.SetText(result.Text);
            ownedClipboard = Native.GetClipboardSequenceNumber();
            restoreClipboard = true;
            Keyboard.Chord(Native.VkControl, Native.VkV);
            await Task.Delay(PasteSettle, token);
            // Many apps do not report read-only text. If the original selection is still selected after
            // pasting, the paste was ignored, so show the translation instead.
            if (mode == CaptureMode.Selection && guard.CanReplace)
            {
                string? afterPaste = await CopySelectionAsync(selectAll: false, SelectionProbe);
                if (Capture.PasteWasIgnored(copied, afterPaste)) ShowBubble();
            }
        }
        catch (OperationCanceledException) when (guard?.Token.IsCancellationRequested == true)
        {
            // User cancellation is not an engine failure and must not start the fallback or log an error.
        }
        catch (TranslationFailedException failure)
        {
            ErrorLog.Write(settings.Primary.DisplayName + " failed", failure.Primary);
            if (failure.Fallback is { } fallback) ErrorLog.Write(settings.Fallback.DisplayName + " failed", fallback);
            var provider = EngineCli.IsSignInError(failure.Primary) ? settings.Primary.Provider
                : failure.Fallback is { } other && EngineCli.IsSignInError(other) ? settings.Fallback.Provider
                : Provider.None;
            if (provider != Provider.None)
                NotifySignIn(provider, text.T("{0} needs you to sign in. Click here to sign in.", EngineCli.ProductName(provider)));
            else
                Notify(text.T("Translation failed: {0}", failure.Message), ToolTipIcon.Error);
        }
        catch (Exception error)
        {
            // Last line of defence for a hotkey handler: report instead of crashing the tray app.
            ErrorLog.Write("Translation hotkey failed", error);
            Notify(text.T("Translation failed: {0}", error.Message), ToolTipIcon.Error);
        }
        finally
        {
            indicator.Hide();
            if (restoreClipboard && snapshot is not null && Native.GetClipboardSequenceNumber() == ownedClipboard)
                RestoreClipboard(snapshot);
            guard?.Dispose();
            busy = false;
        }
    }

    private static async Task<string?> CopyAsync(bool selectAll, TimeSpan wait, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        uint before = Native.GetClipboardSequenceNumber();
        if (selectAll) Keyboard.Chord(Native.VkControl, Native.VkA);
        Keyboard.Chord(Native.VkControl, Native.VkC);
        var clock = Stopwatch.StartNew();
        while (Native.GetClipboardSequenceNumber() == before)
        {
            if (clock.Elapsed > wait) return null;
            await Task.Delay(15, token);
        }
        token.ThrowIfCancellationRequested();
        return Clipboard.ContainsText() ? Clipboard.GetText() : "";
    }

    private static void RestoreClipboard(ClipboardSnapshot snapshot)
    {
        try { snapshot.Restore(); }
        catch (System.Runtime.InteropServices.ExternalException error) { ErrorLog.Write("Could not restore the clipboard", error); }
    }

    private Task RequestUpdateCheck(bool manual = false)
    {
        if (checkingUpdates)
        {
            settingsForm?.SetUpdateCheckStatus(text.T("Checking for updates…"), checking: true);
            return CurrentUpdateCheck;
        }
        return CurrentUpdateCheck = CheckForUpdateAsync(manual);
    }

    private async Task CheckForUpdateAsync(bool manual)
    {
        if ((!manual && !settings.CheckForUpdates) || installing) return;
        checkingUpdates = true;
        checkUpdatesItem.Enabled = false;
        checkUpdatesItem.Text = text.T("Checking for updates…");
        settingsForm?.SetUpdateCheckStatus(text.T("Checking for updates…"), checking: true);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var latest = await fetchUpdate(timeout.Token);
            ShowUpdate(latest is not null && latest.Version > UpdateChecker.Current ? latest : null);
            string message = update is not null ? text.T("Update available: {0}.", update.Tag)
                : text.T("You are using the latest version ({0}).", UpdateChecker.Current.ToString(3));
            settingsForm?.SetUpdateCheckStatus(message);
            if (manual && settingsForm is null) Notify(message, ToolTipIcon.Info);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        {
            ErrorLog.Write("Update check failed", error);
            string message = text.T("Update check failed: {0}", error.Message);
            settingsForm?.SetUpdateCheckStatus(message, error: true);
            if (manual && settingsForm is null) Notify(message, ToolTipIcon.Warning);
        }
        finally
        {
            checkingUpdates = false;
            checkUpdatesItem.Text = text.T("Check for updates");
            checkUpdatesItem.Enabled = !installing;
        }
    }

    private void ShowUpdate(ReleaseInfo? release)
    {
        bool isNew = release is not null && release.Tag != update?.Tag;
        update = release;
        updateItem.Visible = release is not null;
        settingsForm?.SetUpdate(release);
        RefreshTexts();
        if (isNew) Notify(text.T("Inline Translator {0} is available. Right-click the tray icon and choose Update.", release!.Tag), ToolTipIcon.Info);
    }

    private async Task InstallUpdateAsync()
    {
        if (update is null || installing) return;
        string? exe = UpdateInstaller.InstallablePath();
        if (!settings.AutoInstallUpdates || exe is null || update.PackageUrl is null)
        {
            Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true })?.Dispose();
            return;
        }
        installing = true;
        checkUpdatesItem.Enabled = false;
        try
        {
            Notify(text.T("Updating to {0}…", update.Tag), ToolTipIcon.Info);
            await UpdateInstaller.InstallAsync(update, exe, UpdateInstaller.DownloadAsync, CancellationToken.None);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "--wait-pid", Environment.ProcessId.ToString() } })?.Dispose();
            ExitThread();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException
            or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("Update failed", error);
            Notify(text.T("Automatic update failed: {0}", error.Message), ToolTipIcon.Error);
        }
        finally
        {
            installing = false;
            checkUpdatesItem.Enabled = !checkingUpdates;
        }
    }

    private void Notify(string message, ToolTipIcon kind)
    {
        balloonAction = null;
        tray.ShowBalloonTip(5000, "Inline Translator", message, kind);
    }

    private void NotifySignIn(Provider provider, string message)
    {
        tray.ShowBalloonTip(8000, "Inline Translator", message, ToolTipIcon.Warning);
        balloonAction = () =>
        {
            try
            {
                EngineCli.StartSignIn(provider).Dispose();
                signInReminded.Remove(provider);
            }
            catch (Exception error) when (error is EngineException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                ErrorLog.Write("Could not start sign-in", error);
                ShowSettings();
            }
        };
    }

    protected override void ExitThreadCore()
    {
        settingsForm?.Close();
        updateTimer.Dispose();
        hotkeys.Dispose();
        tray.Visible = false;
        tray.Dispose();
        indicator.Dispose();
        bubble.Dispose();
        icon.Dispose();
        base.ExitThreadCore();
    }
}
