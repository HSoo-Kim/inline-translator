using System.Diagnostics;

namespace InlineTranslator;

internal sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan SelectionProbe = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan CopyWait = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(400);

    private readonly string settingsPath;
    private readonly UiText text;
    private readonly Icon icon = AppIcon.Load();
    private readonly NotifyIcon tray;
    private readonly HotkeyWindow hotkeys;
    private readonly BusyIndicator indicator = new();
    private readonly Translator translator = new(EngineCli.TranslateAsync);
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = (int)TimeSpan.FromHours(24).TotalMilliseconds };
    private readonly ToolStripMenuItem updateItem = new() { Visible = false };
    private readonly ToolStripMenuItem settingsItem = new();
    private readonly ToolStripMenuItem pauseItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem exitItem = new();
    private readonly ToolStripLabel titleItem = new() { Enabled = false };
    private AppSettings settings;
    private SettingsForm? settingsForm;
    private ReleaseInfo? update;
    private bool busy, installing;

    public TrayApp(string settingsPath, AppSettings settings, bool firstRun, uint activationMessage)
    {
        this.settingsPath = settingsPath;
        this.settings = settings;
        text = new UiText(settings.UiLanguage);
        hotkeys = new HotkeyWindow(activationMessage);
        hotkeys.HotkeyPressed += id => _ = TranslateActiveAsync(id - 1);
        hotkeys.ActivationRequested += ShowSettings;

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([titleItem, new ToolStripSeparator(), updateItem, settingsItem, pauseItem, new ToolStripSeparator(), exitItem]);
        updateItem.Click += async (_, _) => await InstallUpdateAsync();
        settingsItem.Click += (_, _) => ShowSettings();
        pauseItem.CheckedChanged += (_, _) => ApplyHotkeys(notify: true);
        exitItem.Click += (_, _) => ExitThread();
        tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        RefreshTexts();
        ApplyHotkeys(notify: true);
        updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        updateTimer.Start();
        _ = CheckForUpdateAsync();
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
        pauseItem.Text = text.T("Pause hotkeys");
        exitItem.Text = text.T("Exit");
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
            for (int i = 0; i < settings.Presets.Count; i++)
            {
                var preset = settings.Presets[i];
                if (preset.Enabled && Hotkey.TryParse(preset.Hotkey, out var hotkey) && !hotkeys.Register(i + 1, hotkey))
                    failed.Add(preset.Hotkey);
            }
        RefreshTexts();
        if (notify && failed.Count > 0)
            Notify(text.T("{0} is already used by another program. Choose another hotkey in Settings.", string.Join(", ", failed)), ToolTipIcon.Warning);
        return failed;
    }

    private void ShowSettings()
    {
        if (settingsForm is { IsDisposed: false })
        {
            settingsForm.Activate();
            return;
        }
        settingsForm = new SettingsForm(settings, text, icon, update, SaveSettings, InstallUpdateAsync);
        settingsForm.FormClosed += (_, _) => settingsForm = null;
        settingsForm.Show();
        settingsForm.Activate();
    }

    private IReadOnlyList<string> SaveSettings(AppSettings updated)
    {
        settings = updated.Normalize();
        SettingsStore.Save(settingsPath, settings);
        text.SetLanguage(settings.UiLanguage);
        if (settings.CheckForUpdates) _ = CheckForUpdateAsync();
        else ShowUpdate(null);
        return ApplyHotkeys(notify: false);
    }

    private async Task TranslateActiveAsync(int presetIndex)
    {
        if (busy || installing || presetIndex < 0 || presetIndex >= settings.Presets.Count) return;
        var preset = settings.Presets[presetIndex];
        var target = Languages.Find(preset.TargetLanguage);
        if (!preset.Enabled || target is null) return;
        busy = true;
        ClipboardSnapshot? snapshot = null;
        bool restoreClipboard = false;
        try
        {
            await Keyboard.WaitForModifiersReleasedAsync(TimeSpan.FromSeconds(2));
            nint window = Native.GetForegroundWindow();
            snapshot = ClipboardSnapshot.Take();
            string? copied = await CopyAsync(selectAll: false, SelectionProbe);
            restoreClipboard = copied is not null;
            var mode = Capture.Decide(!string.IsNullOrWhiteSpace(copied), ForegroundWindow.ProcessName(window), settings.WholeInputApps);
            if (mode == CaptureMode.WholeInput)
            {
                copied = await CopyAsync(selectAll: true, CopyWait);
                restoreClipboard |= copied is not null;
            }
            if (mode == CaptureMode.NeedSelection || string.IsNullOrWhiteSpace(copied))
            {
                Notify(text.T("Select the text to translate first, then press the hotkey."), ToolTipIcon.Info);
                return;
            }

            indicator.ShowNear(ForegroundWindow.CaretOrCursor(window), text.T("Translating to {0}…", target.NativeName));
            var result = await translator.TranslateAsync(settings, target, copied, CancellationToken.None);
            indicator.Hide();
            if (result.PrimaryError is { } primaryError)
                ErrorLog.Write($"{settings.Primary.DisplayName} failed, used {result.Engine.DisplayName}", primaryError);

            restoreClipboard = true;
            PrivateClipboard.SetText(result.Text);
            if (Native.GetForegroundWindow() != window)
            {
                restoreClipboard = false;
                Notify(text.T("You switched windows, so the translation was copied instead. Paste it with Ctrl+V."), ToolTipIcon.Info);
                return;
            }
            if (mode == CaptureMode.WholeInput) Keyboard.Chord(Native.VkControl, Native.VkA);
            Keyboard.Chord(Native.VkControl, Native.VkV);
            await Task.Delay(PasteSettle);
        }
        catch (TranslationFailedException failure)
        {
            ErrorLog.Write(settings.Primary.DisplayName + " failed", failure.Primary);
            if (failure.Fallback is { } fallback) ErrorLog.Write(settings.Fallback.DisplayName + " failed", fallback);
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
            if (restoreClipboard && snapshot is not null) RestoreClipboard(snapshot);
            busy = false;
        }
    }

    private static async Task<string?> CopyAsync(bool selectAll, TimeSpan wait)
    {
        uint before = Native.GetClipboardSequenceNumber();
        if (selectAll) Keyboard.Chord(Native.VkControl, Native.VkA);
        Keyboard.Chord(Native.VkControl, Native.VkC);
        var clock = Stopwatch.StartNew();
        while (Native.GetClipboardSequenceNumber() == before)
        {
            if (clock.Elapsed > wait) return null;
            await Task.Delay(15);
        }
        return Clipboard.ContainsText() ? Clipboard.GetText() : "";
    }

    private static void RestoreClipboard(ClipboardSnapshot snapshot)
    {
        try { snapshot.Restore(); }
        catch (System.Runtime.InteropServices.ExternalException error) { ErrorLog.Write("Could not restore the clipboard", error); }
    }

    private async Task CheckForUpdateAsync()
    {
        if (!settings.CheckForUpdates) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var latest = await UpdateChecker.FetchLatestAsync(timeout.Token);
            ShowUpdate(latest is not null && latest.Version > UpdateChecker.Current ? latest : null);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        {
            ErrorLog.Write("Update check failed", error);
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
        }
    }

    private void Notify(string message, ToolTipIcon kind) => tray.ShowBalloonTip(5000, "Inline Translator", message, kind);

    protected override void ExitThreadCore()
    {
        settingsForm?.Close();
        updateTimer.Dispose();
        hotkeys.Dispose();
        tray.Visible = false;
        tray.Dispose();
        indicator.Dispose();
        icon.Dispose();
        base.ExitThreadCore();
    }
}
