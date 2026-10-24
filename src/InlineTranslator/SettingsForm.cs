using System.Diagnostics;

namespace InlineTranslator;

internal sealed class SettingsForm : Form
{
    private static readonly Provider[] Providers = [Provider.Claude, Provider.Codex];
    private readonly UiText text;
    private readonly AppSettings original;
    private readonly bool originalStartup;
    private readonly Func<AppSettings, IReadOnlyList<string>> save;
    private readonly EngineRun testRun;
    private readonly EngineRow primary, fallback;
    private readonly List<(ToggleSwitch Enabled, HotkeyBox Hotkey, PillDropdown Language)> presets = [];
    private readonly List<Label> hotkeyErrors = [];
    private readonly Dictionary<int, string> registrationErrors = [];
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, SizeMode = TabSizeMode.Fixed };
    private readonly List<(Panel Scroller, FluidStack Page)> pages = [];
    private readonly ToggleSwitch readingHotkeyEnabled;
    private readonly HotkeyBox readingHotkey;
    private readonly PillDropdown tone = Dropdown("tone");
    private readonly PillDropdown homeLanguage = Dropdown("home-language");
    private readonly PillDropdown readingLanguage = Dropdown("reading-language");
    private readonly PillDropdown uiLanguage = Dropdown("ui-language");
    private readonly PillDropdown bubbleTheme = Dropdown("bubble-theme");
    private readonly PillDropdown bubblePosition = Dropdown("bubble-position");
    private readonly FlowLayoutPanel appChips = new()
    {
        AutoSize = false, WrapContents = true, BackColor = Ui.Card, Margin = Padding.Empty,
    };
    private readonly List<string> wholeInputApps = [];
    private readonly ToggleSwitch startWithWindows, checkForUpdates, autoInstall;
    private readonly Dictionary<Provider, SignIn?> signIn = new() { [Provider.Claude] = null, [Provider.Codex] = null };
    private readonly Dictionary<Provider, (StatusPill Pill, PillButton Action)> accounts = [];
    private readonly PillButton saveButton;
    private readonly Label saveError;
    private readonly PillButton updateButton = new("", PillKind.Soft) { Visible = false, Name = "update" };
    private readonly PillButton checkUpdateButton = new("", PillKind.Soft) { Name = "check-update" };
    private readonly Label updateStatus = new() { AutoSize = true, Visible = false, Name = "update-status", ForeColor = Ui.Muted };
    private readonly ToolTip tips = new();
    private bool loading = true, reflowing, sizingTabs;

    internal Task InitialSignIn { get; private set; } = Task.CompletedTask;
    internal Task LastTest { get; private set; } = Task.CompletedTask;
    internal IReadOnlyList<string> TabIds { get; } = ["translation", "engines", "general"];
    internal string SelectedTabId => TabIds[tabs.SelectedIndex < 0 ? 0 : tabs.SelectedIndex];
    internal Panel ActiveScroller => pages[tabs.SelectedIndex < 0 ? 0 : tabs.SelectedIndex].Scroller;
    internal bool HasChanges { get; private set; }
    internal bool HasValidationErrors { get; private set; }

    public SettingsForm(AppSettings settings, UiText text, Icon icon, ReleaseInfo? update,
        Func<AppSettings, IReadOnlyList<string>> save, Func<Task> installUpdate, Action<string> changeLanguage,
        EngineRun? testRun = null, Func<Task>? checkUpdates = null)
    {
        this.text = text;
        original = settings;
        originalStartup = StartWithWindows.IsEnabled;
        this.save = save;
        this.testRun = testRun ?? EngineCli.TranslateAsync;
        Text = "Inline Translator - " + text.T("Settings");
        Icon = icon;
        Font = Ui.Regular();
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Canvas;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 680);
        Disposed += (_, _) => tips.Dispose();

        startWithWindows = Toggle(text.T("Start with Windows"), "start-windows");
        startWithWindows.Enabled = StartWithWindows.Executable is not null;
        checkForUpdates = Toggle(text.T("Check for new versions"), "check-updates");
        autoInstall = Toggle(text.T("Install automatically when Update is clicked"), "auto-install");
        readingHotkeyEnabled = Toggle(text.T("Read-only translation hotkey"), "reading-enabled");
        readingHotkey = new HotkeyBox(text) { Name = "reading-hotkey", AccessibleName = text.T("Read-only translation hotkey") };
        primary = new EngineRow("primary", text.T("Primary"), false, text, this);
        fallback = new EngineRow("fallback", text.T("If it fails"), true, text, this);
        foreach (var engine in new[] { primary, fallback })
        {
            engine.Test.Click += (_, _) =>
            {
                LastTest = TestAsync(engine);
                engine.Test.Tag = LastTest;
            };
            engine.Changed += () =>
            {
                engine.ShowStatus(signIn);
                RefreshDraftState();
            };
        }

        var translation = AddTab("translation", text.T("Translation"));
        var languageCard = Card(text.T("Languages and tone"));
        homeLanguage.SetOptions(new (object, string)[]
        {
            ("", text.T("System default") + " · " + Languages.FromCulture(System.Globalization.CultureInfo.CurrentUICulture).NativeName),
        }.Concat(Languages.All.Select(item => ((object)item.Code, item.ToString()))));
        languageCard.Controls.Add(Row(text.T("My language"), homeLanguage));
        languageCard.Controls.Add(Note(text.T("Your language and the hotkey language translate in both directions.")));
        tone.SetOptions(new (object, string)[]
        {
            ("natural", text.T("Match the original")), ("casual", text.T("Casual")), ("formal", text.T("Polite")),
        });
        languageCard.Controls.Add(Row(text.T("Tone"), tone));
        translation.Controls.Add(languageCard);

        var writingCard = Card(text.T("Hotkeys and target languages"),
            text.T("Select text and press a hotkey: it is replaced with the translation in place."));
        for (int i = 0; i < AppSettings.PresetCount; i++)
        {
            int index = i;
            var enabled = Toggle(text.T("Writing hotkey {0}", i + 1), $"preset-{i}-enabled");
            var hotkey = new HotkeyBox(text) { Name = $"preset-{i}-hotkey", AccessibleName = text.T("Writing hotkey {0}", i + 1) };
            var language = Dropdown($"preset-{i}-language");
            language.SetOptions(Languages.All.Select(item => ((object)item.Code, item.ToString())));
            var group = new FluidStack { Margin = new Padding(0, 8, 0, 8) };
            group.Controls.Add(enabled);
            group.Controls.Add(Row(text.T("Hotkey"), HotkeyField(hotkey)));
            var error = ErrorLabel($"preset-{i}-error");
            hotkeyErrors.Add(error);
            group.Controls.Add(error);
            group.Controls.Add(Row(text.T("Target language"), language));
            enabled.CheckedChanged += (_, _) =>
            {
                language.Enabled = enabled.Checked;
                hotkey.ForeColor = enabled.Checked ? Ui.Ink : Ui.Gray;
                registrationErrors.Remove(index);
                RefreshDraftState();
            };
            hotkey.TextChanged += (_, _) => HotkeyEdited(index);
            writingCard.Controls.Add(group);
            presets.Add((enabled, hotkey, language));
        }
        translation.Controls.Add(writingCard);

        var readingCard = Card(text.T("Read-only translation hotkey"));
        readingCard.Controls.Add(readingHotkeyEnabled);
        readingCard.Controls.Add(Row(text.T("Hotkey"), HotkeyField(readingHotkey)));
        readingLanguage.SetOptions(Languages.All.Select(language => ((object)language.Code, language.ToString())));
        readingCard.Controls.Add(Row(text.T("Target language"), readingLanguage));
        var readingError = ErrorLabel("reading-error");
        hotkeyErrors.Add(readingError);
        readingCard.Controls.Add(readingError);
        readingCard.Controls.Add(Note(text.T("Text in My language goes to the selected language; other text comes to My language. The original stays unchanged.")));
        readingHotkeyEnabled.CheckedChanged += (_, _) =>
        {
            readingHotkey.ForeColor = readingHotkeyEnabled.Checked ? Ui.Ink : Ui.Gray;
            readingLanguage.Enabled = readingHotkeyEnabled.Checked;
            registrationErrors.Remove(AppSettings.PresetCount);
            RefreshDraftState();
        };
        readingHotkey.TextChanged += (_, _) => HotkeyEdited(AppSettings.PresetCount);
        translation.Controls.Add(readingCard);

        var engines = AddTab("engines", text.T("Engines"));
        engines.Controls.Add(primary.Panel);
        engines.Controls.Add(fallback.Panel);
        var accountCard = Card(text.T("Accounts"),
            text.T("Uses the Claude Code or Codex you are already signed in to on this PC. No API key needed."));
        foreach (var provider in Providers)
        {
            var pill = new StatusPill();
            var action = new PillButton("", PillKind.Soft) { Visible = false, Margin = new Padding(0, 4, 0, 8) };
            action.Click += (_, _) => AccountAction(provider);
            accountCard.Controls.Add(pill);
            accountCard.Controls.Add(action);
            accounts[provider] = (pill, action);
        }
        engines.Controls.Add(accountCard);

        var general = AddTab("general", text.T("General"));
        var appsCard = Card(text.T("Apps that translate the whole input box"),
            text.T("In these apps, pressing the hotkey with nothing selected translates the whole message. Drag-select first to translate only part of it."));
        appsCard.Controls.Add(appChips);
        general.Controls.Add(appsCard);
        var appearance = Card(text.T("Read-only translation bubble"));
        bubbleTheme.SetOptions(new (object, string)[] { ("light", text.T("White background, black text")), ("dark", text.T("Black background, white text")) });
        bubblePosition.SetOptions(new (object, string)[] { ("mouse", text.T("Follows the mouse")), ("selection", text.T("Right above or below the selected text")) });
        appearance.Controls.Add(Row(text.T("Appearance"), bubbleTheme));
        appearance.Controls.Add(Row(text.T("Bubble position"), bubblePosition));
        general.Controls.Add(appearance);
        var preferences = Card(text.T("General"));
        uiLanguage.SetOptions(DisplayLanguages.Select(language => ((object)language.Code, language.Code == "" ? text.T("System default") : language.Name)));
        preferences.Controls.Add(Row(LanguageLabel(text), uiLanguage));
        preferences.Controls.Add(Note(text.T("Display language changes after Save. Reopen Settings to see it.")));
        preferences.Controls.Add(startWithWindows);
        preferences.Controls.Add(checkForUpdates);
        preferences.Controls.Add(autoInstall);
        checkUpdateButton.Text = text.T("Check for updates");
        checkUpdateButton.AccessibleName = checkUpdateButton.Text;
        checkUpdateButton.Visible = checkUpdates is not null;
        checkUpdateButton.Click += async (_, _) =>
        {
            SetUpdateCheckStatus(text.T("Checking for updates…"), checking: true);
            if (SelectedTabId == "general") ActiveScroller.ScrollControlIntoView(updateStatus);
            await checkUpdates!();
            if (!IsDisposed && SelectedTabId == "general") ActiveScroller.ScrollControlIntoView(updateStatus);
        };
        preferences.Controls.Add(checkUpdateButton);
        preferences.Controls.Add(updateStatus);
        general.Controls.Add(preferences);

        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Ui.Canvas };
        var title = Ui.Text(text.T("Settings"), Ui.Ink, Ui.Bold(14f));
        title.Location = new Point(20, 12);
        header.Controls.Add(title);
        tabs.Name = "settings-tabs";
        tabs.AccessibleName = text.T("Settings");
        tabs.SelectedIndexChanged += (_, _) => ReflowPages();
        tabs.Resize += (_, _) =>
        {
            if (sizingTabs) return;
            sizingTabs = true;
            try
            {
                var itemSize = new Size(Math.Max(1, (tabs.ClientSize.Width - Ui.Scale(tabs, 8)) / 3),
                    Math.Max(36, TextRenderer.MeasureText("Settings", tabs.Font).Height + 12));
                if (tabs.ItemSize != itemSize) tabs.ItemSize = itemSize;
            }
            finally { sizingTabs = false; }
            ReflowPages();
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Ui.Card };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Line);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var version = new LinkLabel
        {
            Text = "v" + UpdateChecker.Current.ToString(3) + " · GitHub", AutoSize = true,
            LinkColor = Ui.Muted, ActiveLinkColor = Ui.Accent, LinkBehavior = LinkBehavior.HoverUnderline,
        };
        version.LinkClicked += (_, _) => Open("https://github.com/HSoo-Kim/inline-translator");
        saveButton = new PillButton(text.T("Save"), PillKind.Primary)
        {
            Name = "save", AccessibleName = text.T("Save"), MinimumSize = new Size(0, 36), Enabled = false,
        };
        var cancelButton = new PillButton(text.T("Cancel"), PillKind.Secondary)
        {
            Name = "cancel", AccessibleName = text.T("Cancel"), MinimumSize = new Size(0, 36), DialogResult = DialogResult.Cancel,
        };
        saveButton.Click += (_, _) => Save();
        cancelButton.Click += (_, _) => Close();
        updateButton.Click += async (_, _) => await installUpdate();
        footer.Controls.AddRange([version, updateButton, cancelButton, saveButton]);
        footer.Layout += (_, _) =>
        {
            int buttonsWidth = new[] { updateButton, cancelButton, saveButton }.Where(button => button.Visible)
                .Sum(button => Math.Max(button.GetPreferredSize(Size.Empty).Width, button.MinimumSize.Width) + Ui.Scale(footer, 8));
            version.Visible = footer.ClientSize.Width >= buttonsWidth + version.PreferredWidth + Ui.Scale(footer, 48);
            Ui.LayoutFooter(footer, version.Visible ? version : null, [updateButton, cancelButton, saveButton]);
        };
        saveError = ErrorLabel("save-error");
        saveError.Dock = DockStyle.Bottom;
        saveError.Padding = new Padding(20, 8, 20, 8);
        saveError.AutoSize = false;
        Controls.Add(tabs);
        Controls.Add(saveError);
        Controls.Add(header);
        Controls.Add(footer);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadSettings(settings);
        foreach (var dropdown in new[] { tone, homeLanguage, readingLanguage, uiLanguage, bubbleTheme, bubblePosition }.Concat(presets.Select(row => row.Language)))
            dropdown.SelectionChanged += RefreshDraftState;
        foreach (var toggle in new[] { startWithWindows, checkForUpdates, autoInstall })
            toggle.CheckedChanged += (_, _) =>
            {
                autoInstall.Enabled = checkForUpdates.Checked;
                RefreshDraftState();
            };
        loading = false;
        RefreshDraftState();
        SetUpdate(update);
        Shown += async (_, _) => await (InitialSignIn = RefreshSignInAsync());
    }

    public static readonly IReadOnlyList<(string Code, string Name)> DisplayLanguages =
        [("", "System default"), ("ko", "한국어"), ("en", "English"), ("zh-CN", "简体中文"), ("ja", "日本語")];

    public static string LanguageLabel(UiText text) =>
        text.Language == "en" ? text.T("Display language") : text.T("Display language") + " (Language)";

    internal void SelectTab(string id)
    {
        int index = TabIds.ToList().IndexOf(id);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(id));
        tabs.SelectedIndex = index;
        ReflowPages();
    }

    public void SetUpdate(ReleaseInfo? release)
    {
        updateButton.Visible = release is not null;
        if (release is not null)
        {
            updateButton.Text = text.T("Update");
            tips.SetToolTip(updateButton, text.T("Update to {0}", release.Tag));
        }
        updateButton.Parent?.PerformLayout();
    }

    internal void SetUpdateCheckStatus(string message, bool checking = false, bool error = false)
    {
        checkUpdateButton.Enabled = !checking;
        updateStatus.Text = message;
        updateStatus.ForeColor = error ? Ui.Red : Ui.Muted;
        updateStatus.Visible = true;
        ReflowPages();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.RoundWindow(this);
    }

    protected override void OnShown(EventArgs e)
    {
        FitToWorkingArea(Screen.FromControl(this).WorkingArea);
        ReflowPages();
        base.OnShown(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible) BeginInvoke(() => FitToWorkingArea(Screen.FromControl(this).WorkingArea));
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x007E && Visible)
            BeginInvoke(() => FitToWorkingArea(Screen.FromControl(this).WorkingArea));
    }

    internal void FitToWorkingArea(Rectangle area)
    {
        int margin = Math.Min(Ui.Scale(this, 12), Math.Min(area.Width, area.Height) / 10);
        var available = Rectangle.Inflate(area, -margin, -margin);
        MinimumSize = new Size(Math.Min(available.Width, Math.Max(480, Ui.Scale(this, 280))),
            Math.Min(available.Height, Math.Max(300, Ui.Scale(this, 180))));
        if (WindowState == FormWindowState.Maximized) return;
        var size = new Size(Math.Clamp(Width, MinimumSize.Width, available.Width),
            Math.Clamp(Height, MinimumSize.Height, available.Height));
        Bounds = new Rectangle(Math.Clamp(Left, available.Left, available.Right - size.Width),
            Math.Clamp(Top, available.Top, available.Bottom - size.Height), size.Width, size.Height);
    }

    private FluidStack AddTab(string id, string label)
    {
        var tab = new TabPage(label) { Name = id, BackColor = Ui.Canvas, Padding = Padding.Empty };
        var scroller = new Panel { Name = id + "-scroll", Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Canvas };
        var page = new FluidStack { Padding = new Padding(12), BackColor = Ui.Canvas };
        scroller.Controls.Add(page);
        tab.Controls.Add(scroller);
        tabs.TabPages.Add(tab);
        pages.Add((scroller, page));
        scroller.ClientSizeChanged += (_, _) => ReflowPages();
        scroller.VisibleChanged += (_, _) => { if (scroller.Visible) ReflowPages(); };
        return page;
    }

    private void ReflowPages()
    {
        if (reflowing) return;
        reflowing = true;
        try
        {
            foreach (var (scroller, page) in pages)
            {
                if (!scroller.Visible) continue;
                int width = Math.Max(1, scroller.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
                page.Size = page.GetPreferredSize(new Size(width, 0));
                page.PerformLayout();
            }
            if (saveError.Visible)
                saveError.Height = saveError.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
        }
        finally { reflowing = false; }
    }

    private static PillDropdown Dropdown(string name) => new(240)
    {
        Name = name, WrapText = true, Margin = new Padding(0, 4, 0, 4),
    };

    private static ToggleSwitch Toggle(string label, string name) => new(label)
    {
        Name = name, AccessibleName = label, WrapText = true, Margin = new Padding(0, 4, 0, 4),
    };

    private static FluidStack Card(string title, string? description = null)
    {
        var card = new FluidStack(true) { Padding = new Padding(16), Margin = new Padding(0, 0, 0, 12) };
        card.Controls.Add(Ui.Text(title, Ui.Ink, Ui.Bold(11f)));
        if (description is not null) card.Controls.Add(Note(description));
        return card;
    }

    private static Label Note(string value) => Ui.Text(value, Ui.Muted, Ui.Regular());

    private static Label ErrorLabel(string name) => new()
    {
        Name = name, AutoSize = true, ForeColor = Ui.Red, BackColor = Color.Transparent,
        Font = Ui.Regular(), Visible = false, Margin = new Padding(0, 4, 0, 8),
    };

    private static FluidRow Row(string label, Control input)
    {
        input.AccessibleName ??= label;
        return new FluidRow(Ui.Text(label, Ui.Muted), input);
    }

    private static RoundedPanel HotkeyField(HotkeyBox hotkey)
    {
        var field = new RoundedPanel(Ui.Card, Ui.Line, 10)
        {
            Height = Math.Max(36, hotkey.PreferredHeight + 16), MinimumSize = new Size(0, 36), Padding = new Padding(8),
            Margin = new Padding(0, 4, 0, 4),
        };
        field.Controls.Add(hotkey);
        return field;
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    private void LoadSettings(AppSettings settings)
    {
        primary.Set(settings.Primary);
        fallback.Set(settings.Fallback);
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = settings.Presets[i];
            presets[i].Enabled.Checked = preset.Enabled;
            presets[i].Hotkey.SetDraft(preset.Hotkey);
            presets[i].Language.Select(preset.TargetLanguage);
            presets[i].Language.Enabled = preset.Enabled;
            presets[i].Hotkey.ForeColor = preset.Enabled ? Ui.Ink : Ui.Gray;
        }
        readingHotkeyEnabled.Checked = settings.ReadingHotkeyEnabled;
        readingHotkey.SetDraft(settings.ReadingHotkey);
        readingLanguage.Select(settings.ReadingTargetLanguage);
        readingLanguage.Enabled = settings.ReadingHotkeyEnabled;
        readingHotkey.ForeColor = settings.ReadingHotkeyEnabled ? Ui.Ink : Ui.Gray;
        tone.Select(settings.Tone);
        homeLanguage.Select(settings.HomeLanguage);
        bubbleTheme.Select(settings.BubbleTheme);
        bubblePosition.Select(settings.BubblePosition);
        uiLanguage.Select(settings.UiLanguage);
        wholeInputApps.AddRange(settings.WholeInputApps);
        ShowApps();
        startWithWindows.Checked = originalStartup;
        checkForUpdates.Checked = settings.CheckForUpdates;
        autoInstall.Checked = settings.AutoInstallUpdates;
        autoInstall.Enabled = settings.CheckForUpdates;
        foreach (var (provider, row) in accounts)
            tips.SetToolTip(row.Pill, EngineCli.Resolve(provider) ?? EngineCli.ProductName(provider) + ": " + text.T("not found"));
        ShowAccounts();
    }

    private void ShowApps()
    {
        appChips.SuspendLayout();
        foreach (Control control in appChips.Controls.Cast<Control>().ToArray()) control.Dispose();
        foreach (string app in wholeInputApps)
        {
            var chip = new Chip(app) { AccessibleName = app };
            chip.Removed += () =>
            {
                wholeInputApps.Remove(app);
                ShowApps();
            };
            appChips.Controls.Add(chip);
        }
        var add = new PillButton(text.T("+ Add apps"), PillKind.Soft) { Name = "add-apps" };
        add.Click += (_, _) => PickApps();
        appChips.Controls.Add(add);
        appChips.ResumeLayout();
        RefreshDraftState();
    }

    private void PickApps()
    {
        using var picker = new AppPicker(text, wholeInputApps);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        wholeInputApps.Clear();
        wholeInputApps.AddRange(picker.Selected);
        ShowApps();
    }

    private AppSettings Collect() => original with
    {
        Primary = primary.Get(), Fallback = fallback.Get(),
        Presets = presets.Select(row => new HotkeyPreset(row.Enabled.Checked, row.Hotkey.Draft,
            row.Language.SelectedValue as string ?? "en")).ToArray(),
        ReadingHotkeyEnabled = readingHotkeyEnabled.Checked, ReadingHotkey = readingHotkey.Draft,
        ReadingTargetLanguage = readingLanguage.SelectedValue as string ?? original.ReadingTargetLanguage,
        Tone = tone.SelectedValue as string ?? "natural", HomeLanguage = homeLanguage.SelectedValue as string ?? "",
        BubbleTheme = bubbleTheme.SelectedValue as string ?? "light", BubblePosition = bubblePosition.SelectedValue as string ?? "mouse",
        WholeInputApps = wholeInputApps.ToArray(), UiLanguage = uiLanguage.SelectedValue as string ?? "",
        CheckForUpdates = checkForUpdates.Checked, AutoInstallUpdates = autoInstall.Checked,
    };

    private void HotkeyEdited(int index)
    {
        registrationErrors.Remove(index);
        RefreshDraftState();
    }

    private void RefreshDraftState()
    {
        if (loading) return;
        var draft = Collect();
        var errors = SettingsValidation.HotkeyErrors(draft);
        HasChanges = !SettingsStore.Equivalent(original, draft) || startWithWindows.Checked != originalStartup;
        HasValidationErrors = errors.Count > 0 || registrationErrors.Count > 0;
        for (int i = 0; i < hotkeyErrors.Count; i++)
        {
            string message = errors.TryGetValue(i, out var problem) ? text.T(problem switch
            {
                HotkeyProblem.Missing => "Choose a hotkey.",
                HotkeyProblem.Invalid => "Use Ctrl, Alt or Win with a letter, number or function key.",
                _ => "This hotkey is also used by another translation shortcut.",
            }) : registrationErrors.GetValueOrDefault(i, "");
            hotkeyErrors[i].Text = message;
            hotkeyErrors[i].Visible = message.Length > 0;
        }
        saveButton.Enabled = HasChanges && !HasValidationErrors;
        ReflowPages();
    }

    private void Save()
    {
        RefreshDraftState();
        if (!saveButton.Enabled) return;
        var updated = Collect();
        saveError.Visible = false;
        try
        {
            if (startWithWindows.Enabled && startWithWindows.Checked != StartWithWindows.IsEnabled)
                StartWithWindows.Set(startWithWindows.Checked);
            var failed = save(updated);
            if (failed.Count > 0)
            {
                var hotkeys = updated.Presets.Select(preset => (preset.Enabled, preset.Hotkey))
                    .Append((Enabled: updated.ReadingHotkeyEnabled, Hotkey: updated.ReadingHotkey)).ToArray();
                for (int i = 0; i < hotkeys.Length; i++)
                    if (hotkeys[i].Enabled && failed.Contains(hotkeys[i].Hotkey, StringComparer.OrdinalIgnoreCase))
                        registrationErrors[i] = text.T("This hotkey is already used by another app. Choose another.");
                RefreshDraftState();
                int first = registrationErrors.Keys.First();
                SelectTab("translation");
                ActiveScroller.ScrollControlIntoView(hotkeyErrors[first]);
                return;
            }
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ErrorLog.Write("Could not save settings", error);
            saveError.Text = text.T("Could not save settings: {0}", error.Message);
            saveError.Visible = true;
            ReflowPages();
        }
    }

    private async Task TestAsync(EngineRow row)
    {
        var engine = row.Get();
        if (engine.Provider == Provider.None || row.Running) return;
        var target = Languages.Find(presets[0].Language.SelectedValue as string) ?? Languages.All[0];
        string sample = target.Code == "ko" ? "Hi! What time is the meeting tomorrow? I might be a bit late." : "안녕하세요! 내일 회의 몇 시에 하나요? 조금 늦을 것 같아요.";
        var settings = Collect() with { Primary = engine, Fallback = new EngineChoice(Provider.None, "") };
        row.Running = true;
        row.Test.Enabled = false;
        row.Result.Visible = true;
        row.Result.ForeColor = Ui.Muted;
        row.Result.Text = text.T("Testing {0}…", engine.DisplayName);
        ReflowPages();
        if (SelectedTabId == "engines") ActiveScroller.ScrollControlIntoView(row.Result);
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await new Translator(testRun).TranslateAsync(settings, target, sample, CancellationToken.None);
            if (IsDisposed) return;
            row.Result.ForeColor = Ui.GreenInk;
            row.Result.Text = text.T("Test succeeded · {0} · {1:0.0}s\n{2}", engine.DisplayName, clock.Elapsed.TotalSeconds, result.Text);
        }
        catch (TranslationFailedException failure)
        {
            if (IsDisposed) return;
            row.Result.ForeColor = Ui.Red;
            row.Result.Text = text.T("Test failed · {0}", failure.Message);
            if (EngineCli.IsSignInError(failure.Primary)) await RefreshSignInAsync();
        }
        finally
        {
            row.Running = false;
            if (!IsDisposed)
            {
                row.Test.Enabled = row.Get().Provider != Provider.None;
                ReflowPages();
                if (SelectedTabId == "engines") ActiveScroller.ScrollControlIntoView(row.Result);
            }
        }
    }

    private async Task RefreshSignInAsync()
    {
        foreach (var provider in Providers) signIn[provider] = null;
        ShowAccounts();
        var claude = EngineCli.CheckSignInAsync(Provider.Claude, CancellationToken.None);
        var codex = EngineCli.CheckSignInAsync(Provider.Codex, CancellationToken.None);
        signIn[Provider.Claude] = await claude;
        signIn[Provider.Codex] = await codex;
        if (!IsDisposed) ShowAccounts();
    }

    private void ShowAccounts()
    {
        foreach (var (provider, row) in accounts)
        {
            var state = signIn[provider];
            string name = EngineCli.ProductName(provider);
            switch (state)
            {
                case SignIn.SignedIn:
                    row.Pill.Show($"{name} · {text.T("Signed in")}", Ui.Green, Ui.GreenSoft, Ui.GreenInk);
                    break;
                case SignIn.SignedOut:
                    row.Pill.Show($"{name} · {text.T("Sign-in needed")}", Ui.Red, Ui.RedSoft, Ui.Red);
                    break;
                case SignIn.NotInstalled:
                    row.Pill.Show($"{name} · {text.T("Not installed")}", Ui.Gray, Ui.Soft, Ui.Muted);
                    break;
                case SignIn.Unknown:
                    row.Pill.Show($"{name} · {text.T("Status unknown")}", Ui.Gray, Ui.Soft, Ui.Muted);
                    break;
                default:
                    row.Pill.Show($"{name} · {text.T("Checking…")}", Ui.Gray, Ui.Soft, Ui.Muted);
                    break;
            }
            row.Action.Visible = state is SignIn.SignedOut or SignIn.NotInstalled or SignIn.Unknown;
            row.Action.Text = state == SignIn.NotInstalled ? text.T("How to install") : text.T("Sign in");
        }
        primary.ShowStatus(signIn);
        fallback.ShowStatus(signIn);
        ReflowPages();
    }

    private void AccountAction(Provider provider)
    {
        try
        {
            if (signIn[provider] == SignIn.NotInstalled) { Open(EngineCli.InstallPage(provider)); return; }
            var window = EngineCli.StartSignIn(provider);
            window.EnableRaisingEvents = true;
            window.Exited += (_, _) =>
            {
                window.Dispose();
                if (!IsDisposed) BeginInvoke(async () => await RefreshSignInAsync());
            };
        }
        catch (Exception error) when (error is EngineException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("Could not start sign-in", error);
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed class EngineRow
    {
        private readonly PillDropdown provider, model;
        private readonly Label status;
        public FluidStack Panel { get; }
        public PillButton Test { get; }
        public Label Result { get; }
        public bool Running { get; set; }
        public event Action? Changed;

        public EngineRow(string id, string title, bool allowNone, UiText text, IWin32Window owner)
        {
            provider = Dropdown(id + "-provider");
            model = Dropdown(id + "-model");
            Panel = Card(title);
            status = Note("");
            status.Name = id + "-status";
            Panel.Controls.Add(status);
            Panel.Controls.Add(Row(text.T("Provider"), provider));
            Panel.Controls.Add(Row(text.T("Model"), model));
            Test = new PillButton(text.T("Test"), PillKind.Secondary) { Name = id + "-test", AccessibleName = title + " - " + text.T("Test") };
            Panel.Controls.Add(Test);
            Result = Note("");
            Result.Name = id + "-result";
            Result.Visible = false;
            Panel.Controls.Add(Result);
            var providers = new List<(object, string)>();
            if (allowNone) providers.Add((Provider.None, text.T("None")));
            providers.Add((Provider.Claude, "Claude Code"));
            providers.Add((Provider.Codex, "Codex"));
            provider.SetOptions(providers);
            provider.SelectionChanged += () => FillModels(AppSettings.DefaultModel(SelectedProvider));
            model.SelectionChanged += () => Changed?.Invoke();
            model.AllowCustom(text.T("Enter a model name…"), () => InputDialog.Ask(owner, text.T("Model"),
                text.T("Type the exact model name, for example claude-opus-5-5."), text.T("OK"), text.T("Cancel")));
            this.text = text;
        }

        private readonly UiText text;
        private Provider SelectedProvider => provider.SelectedValue is Provider value ? value : Provider.Claude;

        public void Set(EngineChoice engine)
        {
            provider.Select(engine.Provider);
            FillModels(engine.Model);
        }

        public EngineChoice Get() => new(SelectedProvider, SelectedProvider == Provider.None ? "" : model.SelectedValue as string ?? "");

        public void ShowStatus(IReadOnlyDictionary<Provider, SignIn?> states)
        {
            var state = SelectedProvider == Provider.None ? null : states[SelectedProvider];
            status.Text = text.T(SelectedProvider == Provider.None ? "None" : state switch
            {
                SignIn.SignedIn => "Signed in", SignIn.SignedOut => "Sign-in needed",
                SignIn.NotInstalled => "Not installed", SignIn.Unknown => "Status unknown", _ => "Checking…",
            });
            status.ForeColor = state switch { SignIn.SignedIn => Ui.GreenInk, SignIn.SignedOut => Ui.Red, _ => Ui.Muted };
        }

        private void FillModels(string selected)
        {
            var choice = SelectedProvider;
            model.Enabled = choice != Provider.None;
            Test.Enabled = choice != Provider.None && !Running;
            model.SetOptions(EngineCli.SuggestedModels(choice).Select(option =>
                ((object)option.Id, option.Label)));
            if (choice != Provider.None) model.Select(selected);
            else model.SelectIndex(-1);
            Changed?.Invoke();
        }
    }
}

internal sealed class HotkeyBox : TextBox
{
    private readonly UiText text;

    public HotkeyBox(UiText text)
    {
        this.text = text;
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        BackColor = Ui.Card;
        Dock = DockStyle.Fill;
        TextAlign = HorizontalAlignment.Center;
        Font = Ui.Bold();
        Cursor = Cursors.Hand;
    }

    public string Draft => Text == text.T("Press a key combination") ? "" : Text;
    public Hotkey? GetHotkey() => Hotkey.TryParse(Draft, out var value) ? value : null;
    public void SetHotkey(Hotkey? value) => SetDraft(value?.ToString() ?? "");
    public void SetDraft(string value) => Text = value.Length > 0 ? value : text.T("Press a key combination");

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys modifiers = keyData & Keys.Modifiers;
        if (key == Keys.Tab && (modifiers == Keys.None || modifiers == Keys.Shift)) return base.ProcessCmdKey(ref msg, keyData);
        if (modifiers == Keys.None && key is Keys.Back or Keys.Delete or Keys.Escape)
        {
            SetHotkey(null);
            return true;
        }
        if (key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return true;
        var flags = HotkeyModifiers.None;
        if (modifiers.HasFlag(Keys.Control)) flags |= HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(Keys.Alt)) flags |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(Keys.Shift)) flags |= HotkeyModifiers.Shift;
        if (Keyboard.IsDown(Native.VkLWin) || Keyboard.IsDown(Native.VkRWin)) flags |= HotkeyModifiers.Win;
        var candidate = new Hotkey(flags, (uint)key);
        SetDraft(candidate.ToString() is { Length: > 0 } value ? value : key.ToString());
        return true;
    }
}
