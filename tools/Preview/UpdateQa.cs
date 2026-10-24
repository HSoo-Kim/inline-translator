using InlineTranslator;

internal static class UpdateQa
{
    internal static async Task VerifyAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var settings = new AppSettings
        {
            UiLanguage = "ko",
            Presets = AppSettings.DefaultPresets.Select(preset => preset with { Enabled = false }).ToArray(),
            ReadingHotkeyEnabled = false,
        }.Normalize();
        var release = new ReleaseInfo(new Version(0, 1, 99), "v0.1.99",
            "https://github.com/HSoo-Kim/inline-translator/releases");
        var response = Response();
        int requests = 0;
        async Task<ReleaseInfo?> Fetch(CancellationToken token)
        {
            requests++;
            return await response.Task.WaitAsync(token);
        }

        var tray = new TrayApp(Path.Combine(folder, "settings.json"), settings, false,
            Native.RegisterWindowMessage("InlineTranslator.UpdateQA." + Guid.NewGuid()), Fetch);
        try
        {
            Check(requests == 1, "Startup must check for updates.");
            tray.ShowSettings();
            var form = tray.SettingsWindow!;
            await form.InitialSignIn.WaitAsync(Program.QaTimeout);
            form.SelectTab("general");
            var check = Find<Button>(form, "check-update");
            var status = Find<Label>(form, "update-status");
            Check(requests == 1 && !check.Enabled && status.Visible, "Opening Settings must share the running check and show progress.");
            form.ActiveScroller.ScrollControlIntoView(status);
            Capture(form, Path.Combine(folder, "checking.png"));
            response.SetResult(release);
            await tray.CurrentUpdateCheck.WaitAsync(Program.QaTimeout);
            Check(Find<Button>(form, "update").Visible && check.Enabled, "Detected update must expose a visible Update action.");
            Check(status.Text.Contains(release.Tag, StringComparison.Ordinal) && !form.HasChanges, "Update status must show the version without changing settings.");
            Capture(form, Path.Combine(folder, "available.png"));

            response = Response();
            tray.TrayMenu.Items.Find("check-updates", false).Single().PerformClick();
            Check(requests == 2, "Tray must always offer an explicit check action.");
            response.SetResult(new ReleaseInfo(UpdateChecker.Current, "v" + UpdateChecker.Current.ToString(3), release.Url));
            await tray.CurrentUpdateCheck.WaitAsync(Program.QaTimeout);
            Check(!Find<Button>(form, "update").Visible && status.Visible && check.Enabled, "No-newer-version result must be visible and reset stale Update state.");
            Capture(form, Path.Combine(folder, "current.png"));

            response = Response();
            tray.TrayMenu.Show(new Point(100, 100));
            Check(requests == 3, "Opening tray menu must refresh update availability.");
            tray.TrayMenu.Close();
            response.SetResult(release);
            await tray.CurrentUpdateCheck.WaitAsync(Program.QaTimeout);

            response = Response();
            tray.ShowSettings();
            Check(requests == 4, "Reopening Settings must refresh instead of waiting 24 hours.");
            response.SetResult(release);
            await tray.CurrentUpdateCheck.WaitAsync(Program.QaTimeout);
        }
        finally { tray.ExitThread(); tray.Dispose(); }

        response = Response();
        requests = 0;
        tray = new TrayApp(Path.Combine(folder, "manual-settings.json"), settings with { CheckForUpdates = false }, false,
            Native.RegisterWindowMessage("InlineTranslator.UpdateQA." + Guid.NewGuid()), Fetch);
        try
        {
            tray.ShowSettings();
            var form = tray.SettingsWindow!;
            await form.InitialSignIn.WaitAsync(Program.QaTimeout);
            form.SelectTab("general");
            Check(requests == 0, "Disabled automatic checks must not make background requests.");
            Find<Button>(form, "check-update").PerformClick();
            Check(requests == 1, "Manual check must work even when automatic checks are off.");
            response.SetResult(release);
            await tray.CurrentUpdateCheck.WaitAsync(Program.QaTimeout);
            Check(Find<Button>(form, "update").Visible, "Manual check must expose install action.");

            Find<CheckBox>(form, "auto-install").Checked = false;
            Find<Button>(form, "save").PerformClick();
            tray.ShowSettings();
            form = tray.SettingsWindow!;
            await form.InitialSignIn.WaitAsync(Program.QaTimeout);
            form.SelectTab("general");
            Check(requests == 1 && Find<Button>(form, "update").Visible,
                "Saving with automatic checks off must retain a manually discovered update.");

            response = Response();
            Find<CheckBox>(form, "check-updates").Checked = true;
            Find<Button>(form, "save").PerformClick();
            Check(requests == 2, "Enabling automatic checks must check immediately.");
            response.SetResult(release);
            await tray.CurrentUpdateCheck.WaitAsync(Program.QaTimeout);
        }
        finally { tray.ExitThread(); tray.Dispose(); }
        Console.WriteLine("PASS update checks: startup, coalescing, Settings/tray refresh, manual with auto off, status and toggle-on refresh.");
    }

    private static TaskCompletionSource<ReleaseInfo?> Response() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static void Capture(Form form, string path)
    {
        form.Update();
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(path);
        Console.WriteLine(path);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
