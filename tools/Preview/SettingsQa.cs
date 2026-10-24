using InlineTranslator;

internal static class SettingsQa
{
    internal static async Task VerifyAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var settings = new AppSettings { UiLanguage = "ko" }.Normalize();
        var saved = new List<AppSettings>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool failTest = false;
        async Task<string> Run(EngineChoice _, string __, string ___, CancellationToken token)
        {
            if (failTest) throw new EngineException("QA engine offline");
            started.TrySetResult();
            return await response.Task.WaitAsync(token);
        }
        using var icon = AppIcon.Load();
        using var form = new SettingsForm(settings, new UiText("ko"), icon, null,
            draft => { saved.Add(draft); return []; }, () => Task.CompletedTask, _ => { }, Run);
        await Program.ShowAsync(form);
        await form.InitialSignIn.WaitAsync(Program.QaTimeout);
        var save = Find<Button>(form, "save");
        Check(!form.HasChanges && !save.Enabled, "Initial settings must not enable Save.");
        Check(form.TabIds.SequenceEqual(new[] { "translation", "engines", "general" }), "All three settings tabs must exist.");

        form.SelectTab("general");
        var updates = Find<CheckBox>(form, "check-updates");
        updates.Checked = !updates.Checked;
        Check(form.HasChanges && save.Enabled, "Changing a setting enables Save.");
        form.SelectTab("engines");
        form.SelectTab("general");
        Check(updates.Checked != settings.CheckForUpdates, "Tab navigation must keep draft fields.");
        updates.Checked = settings.CheckForUpdates;
        Check(!form.HasChanges && !save.Enabled, "Reverting a setting disables Save.");
        var startup = Find<CheckBox>(form, "start-windows");
        bool originalStartup = startup.Checked;
        startup.Checked = !originalStartup;
        Check(form.HasChanges && save.Enabled, "Startup preference participates in dirty state without writing the registry.");
        startup.Checked = originalStartup;
        Check(!form.HasChanges && !save.Enabled, "Reverting startup preference clears dirty state.");
        var displayLanguage = Find<PillDropdown>(form, "ui-language");
        displayLanguage.Select("ja");
        Check(form.HasChanges && !form.IsDisposed, "Changing display language keeps other draft fields in the current form.");
        form.SelectTab("translation");
        form.SelectTab("general");
        Check((string?)displayLanguage.SelectedValue == "ja", "Pending interface language survives tab navigation.");
        displayLanguage.Select("ko");
        Check(!form.HasChanges && !save.Enabled, "Reverting interface language clears dirty state.");

        form.SelectTab("translation");
        var tone = Find<PillDropdown>(form, "tone");
        tone.Select("formal");
        Check(form.HasChanges && save.Enabled, "Dropdown selection changes enable Save.");
        tone.Select(settings.Tone);
        Check(!form.HasChanges && !save.Enabled, "Reverting dropdown selection disables Save.");
        var readingLanguage = Find<PillDropdown>(form, "reading-language");
        readingLanguage.Select("ja");
        form.SelectTab("engines");
        form.SelectTab("translation");
        Check((string?)readingLanguage.SelectedValue == "ja" && form.HasChanges && save.Enabled,
            "Reading target selection must survive tabs and enable Save.");
        readingLanguage.Select(settings.ReadingTargetLanguage);
        Check(!form.HasChanges && !save.Enabled, "Restoring reading target clears dirty state.");
        var reading = Find<HotkeyBox>(form, "reading-hotkey");
        var originalReading = reading.GetHotkey();
        Hotkey.TryParse(settings.Presets[0].Hotkey, out var duplicate);
        reading.SetHotkey(duplicate);
        Check(form.HasValidationErrors && !save.Enabled, "Reading/writing hotkey conflict must disable Save.");
        var duplicateLabel = Find<Label>(form, "reading-error");
        Check(duplicateLabel.Visible && duplicateLabel.Text.Length > 0, "Conflicting hotkey must have an inline message.");
        form.ActiveScroller.ScrollControlIntoView(duplicateLabel);
        Capture(form, Path.Combine(folder, "hotkey-duplicate.png"));
        reading.SetHotkey(originalReading);
        Check(!form.HasValidationErrors && !form.HasChanges && !save.Enabled, "Fixing duplicate must clear errors and dirty state.");

        var writing = Find<HotkeyBox>(form, "preset-0-hotkey");
        var originalWriting = writing.GetHotkey();
        writing.SetHotkey(null);
        Check(form.HasValidationErrors && !save.Enabled, "Enabled empty shortcut is invalid.");
        writing.SetHotkey(originalWriting);
        Check(!form.HasValidationErrors && !form.HasChanges, "Restored hotkey clears validation.");

        form.SelectTab("engines");
        foreach (var (name, engine) in new[] { ("primary-model", settings.Primary), ("fallback-model", settings.Fallback) })
        {
            var selected = Find<PillDropdown>(form, name);
            string label = EngineCli.SuggestedModels(engine.Provider).Where(option => option.Id == engine.Model)
                .Select(option => option.Label).DefaultIfEmpty(engine.Model).First();
            Check((string?)selected.SelectedValue == engine.Model, "Model display cleanup must preserve the CLI model ID.");
            Check(selected.SelectedLabel == label, "Model field must show its display name once, without an ID line.");
        }
        var primary = Find<Button>(form, "primary-test");
        var primaryResult = Find<Label>(form, "primary-result");
        var fallbackResult = Find<Label>(form, "fallback-result");
        string untouched = fallbackResult.Text;
        primary.PerformClick();
        await started.Task.WaitAsync(Program.QaTimeout);
        Check(!primary.Enabled && primaryResult.Visible && primaryResult.Text.Length > 0, "Own engine row shows progress while test runs.");
        Check(fallbackResult.Text == untouched, "Primary progress must not overwrite fallback result.");
        Capture(form, Path.Combine(folder, "engine-testing.png"));
        response.SetResult("This is a private QA translation.");
        await form.LastTest.WaitAsync(Program.QaTimeout);
        Check(primary.Enabled && primaryResult.Text.Contains("This is a private QA translation.", StringComparison.Ordinal),
            "Engine success must appear beside tested engine.");
        Check(fallbackResult.Text == untouched, "Primary success must not overwrite fallback result.");
        Check(!form.HasChanges && !save.Enabled, "Engine test results must not enable Save.");
        Capture(form, Path.Combine(folder, "engine-success.png"));
        string primarySuccess = primaryResult.Text;
        failTest = true;
        Find<Button>(form, "fallback-test").PerformClick();
        await form.LastTest.WaitAsync(Program.QaTimeout);
        Check(fallbackResult.Text.Contains("QA engine offline", StringComparison.Ordinal), "Failure stays inside its own engine row.");
        Check(primaryResult.Text == primarySuccess && !form.HasChanges && !save.Enabled, "Failure does not erase another result or change settings.");
        Capture(form, Path.Combine(folder, "engine-failure.png"));
        form.SelectTab("general");
        updates.Checked = !updates.Checked;
        save.PerformClick();
        Check(saved.Count == 1 && saved[0].CheckForUpdates != settings.CheckForUpdates, "Save must persist changed draft.");

        using var conflictForm = new SettingsForm(settings, new UiText("ko"), icon, null,
            draft => [draft.Presets[0].Hotkey], () => Task.CompletedTask, _ => { }, Run);
        await Program.ShowAsync(conflictForm);
        await conflictForm.InitialSignIn.WaitAsync(Program.QaTimeout);
        conflictForm.SelectTab("general");
        Find<CheckBox>(conflictForm, "check-updates").Checked = false;
        Find<Button>(conflictForm, "save").PerformClick();
        Check(!conflictForm.IsDisposed && conflictForm.HasValidationErrors,
            "OS registration failures must keep the form open and show inline errors.");
        Capture(conflictForm, Path.Combine(folder, "hotkey-unavailable.png"));
        Hotkey.TryParse("Ctrl+Alt+E", out var replacement);
        Find<HotkeyBox>(conflictForm, "preset-0-hotkey").SetHotkey(replacement);
        Check(!conflictForm.HasValidationErrors && Find<Button>(conflictForm, "save").Enabled,
            "Editing the unavailable shortcut clears its stale registration error.");
        Console.WriteLine("PASS settings tabs, retained drafts, dirty/revert, inline duplicate/missing hotkeys, per-engine progress/success and save.");
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.Controls.Find(name, true).OfType<T>().Single();

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
