using System.Diagnostics;

namespace InlineTranslator;

internal sealed class SettingsForm : Form
{
    private static readonly Color Muted = Color.FromArgb(96, 103, 112);
    private static readonly Color Accent = Color.FromArgb(37, 99, 235);

    private readonly UiText text;
    private readonly AppSettings original;
    private readonly Func<AppSettings, IReadOnlyList<string>> save;
    private readonly Func<Task> installUpdate;
    private readonly EngineRow primary, fallback;
    private readonly List<(CheckBox Enabled, HotkeyBox Hotkey, ComboBox Language)> presets = [];
    private readonly ComboBox tone = Dropdown(180);
    private readonly TextBox wholeInputApps = new() { Width = 552, Margin = new Padding(0, 6, 0, 0) };
    private readonly ComboBox uiLanguage = Dropdown(180);
    private readonly CheckBox startWithWindows = new() { AutoSize = true };
    private readonly CheckBox checkForUpdates = new() { AutoSize = true };
    private readonly CheckBox autoInstall = new() { AutoSize = true, Margin = new Padding(20, 2, 0, 2) };
    private readonly Label engineStatus = Note("");
    private readonly Label testResult = Note("");
    private readonly ToolTip tips = new();
    private readonly Button updateButton = new() { AutoSize = true, Visible = false, FlatStyle = FlatStyle.System };

    public SettingsForm(AppSettings settings, UiText text, Icon icon, ReleaseInfo? update,
        Func<AppSettings, IReadOnlyList<string>> save, Func<Task> installUpdate)
    {
        this.text = text;
        original = settings;
        this.save = save;
        this.installUpdate = installUpdate;
        Text = "Inline Translator — " + text.T("Settings");
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Disposed += (_, _) => tips.Dispose();

        primary = new EngineRow(text.T("Primary"), allowNone: false, text);
        fallback = new EngineRow(text.T("If it fails"), allowNone: true, text);
        primary.Test.Click += async (_, _) => await TestAsync(primary);
        fallback.Test.Click += async (_, _) => await TestAsync(fallback);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(24, 18, 24, 16),
        };
        Controls.Add(root);

        root.Controls.Add(Section(text.T("Translation engine"),
            text.T("Uses the Claude Code or Codex you are already signed in to on this PC. No API key needed.")));
        var engines = Grid(4);
        primary.AddTo(engines);
        fallback.AddTo(engines);
        root.Controls.Add(engines);
        root.Controls.Add(engineStatus);
        root.Controls.Add(testResult);

        root.Controls.Add(Section(text.T("Hotkeys and target languages"),
            text.T("Select text and press a hotkey: it is replaced with the translation in place.")));
        var hotkeyGrid = Grid(4);
        for (int i = 0; i < AppSettings.PresetCount; i++)
        {
            var enabled = new CheckBox { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 6) };
            var hotkey = new HotkeyBox(text) { Width = 170, Margin = new Padding(0, 4, 8, 4) };
            var language = Dropdown(240);
            language.Items.AddRange([.. Languages.All]);
            hotkeyGrid.Controls.Add(enabled);
            hotkeyGrid.Controls.Add(hotkey);
            hotkeyGrid.Controls.Add(new Label { Text = "→", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
            hotkeyGrid.Controls.Add(language);
            presets.Add((enabled, hotkey, language));
        }
        root.Controls.Add(hotkeyGrid);
        var toneRow = Grid(2);
        toneRow.Controls.Add(new Label { Text = text.T("Tone"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
        tone.Items.AddRange([text.T("Match the original"), text.T("Casual"), text.T("Polite")]);
        toneRow.Controls.Add(tone);
        root.Controls.Add(toneRow);

        root.Controls.Add(Section(text.T("Apps that translate the whole input box"),
            text.T("In these apps, pressing the hotkey with nothing selected translates everything in the input box. Separate with commas.")));
        root.Controls.Add(wholeInputApps);

        root.Controls.Add(Section(text.T("General"), null));
        var languageRow = Grid(2);
        languageRow.Controls.Add(new Label { Text = text.T("Display language"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
        uiLanguage.Items.AddRange([text.T("System default"), "한국어", "English", "简体中文", "日本語"]);
        languageRow.Controls.Add(uiLanguage);
        root.Controls.Add(languageRow);
        startWithWindows.Text = text.T("Start with Windows");
        startWithWindows.Enabled = StartWithWindows.Executable is not null;
        checkForUpdates.Text = text.T("Check for new versions");
        autoInstall.Text = text.T("Install automatically when Update is clicked");
        checkForUpdates.CheckedChanged += (_, _) => autoInstall.Enabled = checkForUpdates.Checked;
        root.Controls.Add(startWithWindows);
        root.Controls.Add(checkForUpdates);
        root.Controls.Add(autoInstall);

        var bottom = new TableLayoutPanel
        {
            ColumnCount = 4,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 20, 0, 0),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var version = new LinkLabel { Text = "v" + UpdateChecker.Current.ToString(3) + " · GitHub", AutoSize = true, Anchor = AnchorStyles.Left, LinkColor = Muted };
        version.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/HSoo-Kim/inline-translator") { UseShellExecute = true })?.Dispose();
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        var saveButton = new Button { Text = text.T("Save"), AutoSize = true, MinimumSize = new Size(88, 30), FlatStyle = FlatStyle.System };
        var cancelButton = new Button { Text = text.T("Cancel"), AutoSize = true, MinimumSize = new Size(88, 30), FlatStyle = FlatStyle.System, DialogResult = DialogResult.Cancel };
        saveButton.Click += (_, _) => Save();
        cancelButton.Click += (_, _) => Close();
        updateButton.Click += async (_, _) => await installUpdate();
        buttons.Controls.AddRange([saveButton, cancelButton]);
        bottom.Controls.Add(version, 0, 0);
        bottom.Controls.Add(updateButton, 2, 0);
        bottom.Controls.Add(buttons, 3, 0);
        root.Controls.Add(bottom);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadSettings(settings);
        SetUpdate(update);
    }

    public void SetUpdate(ReleaseInfo? release)
    {
        updateButton.Visible = release is not null;
        if (release is not null) updateButton.Text = text.T("Update to {0}", release.Tag);
    }

    private void LoadSettings(AppSettings settings)
    {
        primary.Set(settings.Primary);
        fallback.Set(settings.Fallback);
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = settings.Presets[i];
            presets[i].Enabled.Checked = preset.Enabled;
            presets[i].Hotkey.SetHotkey(Hotkey.TryParse(preset.Hotkey, out var hotkey) ? hotkey : null);
            presets[i].Language.SelectedItem = Languages.Find(preset.TargetLanguage) ?? Languages.All[0];
        }
        tone.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.Tones, settings.Tone));
        wholeInputApps.Text = string.Join(", ", settings.WholeInputApps);
        uiLanguage.SelectedIndex = Array.IndexOf(AppSettings.UiLanguages, settings.UiLanguage) + 1;
        startWithWindows.Checked = StartWithWindows.IsEnabled;
        checkForUpdates.Checked = settings.CheckForUpdates;
        autoInstall.Checked = settings.AutoInstallUpdates;
        autoInstall.Enabled = settings.CheckForUpdates;
        var found = new[] { Provider.Claude, Provider.Codex }.Select(provider => (provider, path: EngineCli.Resolve(provider))).ToArray();
        engineStatus.Text = string.Join("   ·   ", found.Select(item =>
            EngineCli.ProductName(item.provider) + (item.path is null ? " ✗ " + text.T("not found") : " ✓")));
        tips.SetToolTip(engineStatus, string.Join("\n", found.Select(item => item.path ?? EngineCli.ProductName(item.provider) + ": " + text.T("not found"))));
        testResult.Visible = false;
    }

    private AppSettings Collect() => original with
    {
        Primary = primary.Get(),
        Fallback = fallback.Get(),
        Presets = presets.Select(row => new HotkeyPreset(row.Enabled.Checked, row.Hotkey.GetHotkey()?.ToString() ?? "",
            (row.Language.SelectedItem as TargetLanguage)?.Code ?? "en")).ToArray(),
        Tone = AppSettings.Tones[Math.Max(0, tone.SelectedIndex)],
        WholeInputApps = wholeInputApps.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        UiLanguage = uiLanguage.SelectedIndex <= 0 ? "" : AppSettings.UiLanguages[uiLanguage.SelectedIndex - 1],
        CheckForUpdates = checkForUpdates.Checked,
        AutoInstallUpdates = autoInstall.Checked,
    };

    private void Save()
    {
        var updated = Collect();
        var duplicates = updated.Presets.Where(preset => preset.Enabled).GroupBy(preset => preset.Hotkey)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
        {
            MessageBox.Show(this, text.T("The same hotkey is used twice: {0}", string.Join(", ", duplicates)),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            if (startWithWindows.Enabled && startWithWindows.Checked != StartWithWindows.IsEnabled)
                StartWithWindows.Set(startWithWindows.Checked);
            var failed = save(updated);
            if (failed.Count > 0)
            {
                MessageBox.Show(this, text.T("{0} is already used by another program. Choose another hotkey in Settings.", string.Join(", ", failed)),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ErrorLog.Write("Could not save settings", error);
            MessageBox.Show(this, text.T("Could not save settings: {0}", error.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task TestAsync(EngineRow row)
    {
        var engine = row.Get();
        if (engine.Provider == Provider.None) return;
        var target = presets.Select(preset => preset.Language.SelectedItem as TargetLanguage).FirstOrDefault() ?? Languages.All[0];
        string sample = target.Code == "ko" ? "Hi! What time is the meeting tomorrow? I might be a bit late." : "안녕하세요! 내일 회의 몇 시에 하나요? 조금 늦을 것 같아요.";
        var settings = Collect() with { Primary = engine, Fallback = new EngineChoice(Provider.None, "") };
        row.Test.Enabled = false;
        testResult.Visible = true;
        testResult.ForeColor = Muted;
        testResult.Text = text.T("Testing {0}…", engine.DisplayName);
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await new Translator(EngineCli.TranslateAsync).TranslateAsync(settings, target, sample, CancellationToken.None);
            testResult.ForeColor = Color.FromArgb(21, 128, 61);
            testResult.Text = $"✓ {engine.DisplayName} · {clock.Elapsed.TotalSeconds:0.0}s · {result.Text}";
        }
        catch (TranslationFailedException failure)
        {
            testResult.ForeColor = Color.FromArgb(185, 28, 28);
            testResult.Text = "✗ " + failure.Message;
        }
        finally
        {
            if (!IsDisposed) row.Test.Enabled = true;
        }
    }

    private static TableLayoutPanel Grid(int columns)
    {
        var grid = new TableLayoutPanel { ColumnCount = columns, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    private static Control Section(string title, string? description)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 16, 0, 0) };
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Accent, Margin = Padding.Empty });
        if (description is not null) panel.Controls.Add(Note(description));
        return panel;
    }

    private static Label Note(string value) =>
        new() { Text = value, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Muted, Margin = new Padding(0, 3, 0, 0) };

    private static ComboBox Dropdown(int width) =>
        new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(0, 4, 8, 4) };

    private sealed class EngineRow
    {
        private readonly bool allowNone;
        private readonly Label label;
        private readonly ComboBox provider = Dropdown(150);
        private readonly ComboBox model = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 220, Margin = new Padding(0, 4, 8, 4) };
        public Button Test { get; } = new() { AutoSize = true, FlatStyle = FlatStyle.System, Margin = new Padding(0, 3, 0, 3) };

        public EngineRow(string title, bool allowNone, UiText text)
        {
            this.allowNone = allowNone;
            label = new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) };
            if (allowNone) provider.Items.Add(text.T("None"));
            provider.Items.AddRange(["Claude Code", "Codex"]);
            Test.Text = text.T("Test");
            provider.SelectedIndexChanged += (_, _) => FillModels(AppSettings.DefaultModel(SelectedProvider));
        }

        private Provider SelectedProvider => (Provider)(provider.SelectedIndex + (allowNone ? 0 : 1));

        public void AddTo(TableLayoutPanel grid) => grid.Controls.AddRange([label, provider, model, Test]);

        public void Set(EngineChoice engine)
        {
            provider.SelectedIndex = (int)engine.Provider - (allowNone ? 0 : 1);
            FillModels(engine.Model);
        }

        public EngineChoice Get()
        {
            var selected = SelectedProvider;
            string value = ModelOption.Resolve(model.Items.OfType<ModelOption>(), model.Text);
            return new EngineChoice(selected, selected == Provider.None ? "" : AppSettings.IsValidModel(value) ? value : AppSettings.DefaultModel(selected));
        }

        private void FillModels(string selected)
        {
            model.Items.Clear();
            var choice = SelectedProvider;
            model.Enabled = Test.Enabled = choice != Provider.None;
            if (choice == Provider.None)
            {
                model.Text = "";
                return;
            }
            var options = EngineCli.SuggestedModels(choice);
            model.Items.AddRange([.. options]);
            var match = options.FirstOrDefault(option => option.Id.Equals(selected, StringComparison.OrdinalIgnoreCase));
            if (match is not null) model.SelectedItem = match;
            else model.Text = selected;
        }
    }
}

internal sealed class HotkeyBox : TextBox
{
    private readonly UiText text;
    private Hotkey? hotkey;

    public HotkeyBox(UiText text)
    {
        this.text = text;
        ReadOnly = true;
        BackColor = Color.White;
        Cursor = Cursors.Hand;
    }

    public Hotkey? GetHotkey() => hotkey;

    public void SetHotkey(Hotkey? value)
    {
        hotkey = value;
        Text = value?.ToString() ?? text.T("Press a key combination");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys modifiers = keyData & Keys.Modifiers;
        if (key == Keys.Tab && modifiers == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
        if (modifiers == Keys.None && key is Keys.Back or Keys.Delete or Keys.Escape)
        {
            SetHotkey(null);
            return true;
        }
        var flags = HotkeyModifiers.None;
        if (modifiers.HasFlag(Keys.Control)) flags |= HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(Keys.Alt)) flags |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(Keys.Shift)) flags |= HotkeyModifiers.Shift;
        if (Keyboard.IsDown(Native.VkLWin) || Keyboard.IsDown(Native.VkRWin)) flags |= HotkeyModifiers.Win;
        var candidate = new Hotkey(flags, (uint)key);
        if (candidate.IsValid) SetHotkey(candidate);
        else Text = text.T("Ctrl or Alt + a letter, digit or F-key");
        return true;
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        SetHotkey(hotkey);
    }
}
