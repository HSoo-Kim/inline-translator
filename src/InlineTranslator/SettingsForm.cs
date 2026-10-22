using System.Diagnostics;

namespace InlineTranslator;

internal sealed class SettingsForm : Form
{
    private const int ContentWidth = 600;
    private static readonly Provider[] Providers = [Provider.Claude, Provider.Codex];

    private readonly UiText text;
    private readonly AppSettings original;
    private readonly Func<AppSettings, IReadOnlyList<string>> save;
    private readonly Func<Task> installUpdate;
    private readonly EngineRow primary, fallback;
    private readonly List<(ToggleSwitch Enabled, HotkeyBox Hotkey, PillDropdown Language)> presets = [];
    private readonly PillDropdown tone = new(190);
    private readonly PillDropdown uiLanguage = new(160);
    private readonly PillDropdown bubbleTheme = new(260);
    private readonly FlowLayoutPanel appChips = new()
    {
        AutoSize = true,
        WrapContents = true,
        MaximumSize = new Size(ContentWidth - 40, 0),
        Margin = new Padding(0, 6, 0, 0),
        BackColor = Color.Transparent,
    };
    private readonly List<string> wholeInputApps = [];
    private readonly ToggleSwitch startWithWindows;
    private readonly ToggleSwitch checkForUpdates;
    private readonly ToggleSwitch autoInstall;
    private readonly Dictionary<Provider, SignIn?> signIn = new() { [Provider.Claude] = null, [Provider.Codex] = null };
    private readonly Dictionary<Provider, (StatusPill Pill, PillButton Action)> accounts = [];
    private readonly Label testResult;
    private readonly PillButton updateButton = new("", PillKind.Soft) { Visible = false, Margin = new Padding(0, 0, 8, 0) };
    private readonly ToolTip tips = new();

    public SettingsForm(AppSettings settings, UiText text, Icon icon, ReleaseInfo? update,
        Func<AppSettings, IReadOnlyList<string>> save, Func<Task> installUpdate, Action<string> changeLanguage)
    {
        this.text = text;
        original = settings;
        this.save = save;
        this.installUpdate = installUpdate;
        Text = "Inline Translator — " + text.T("Settings");
        Icon = icon;
        Font = Ui.Regular();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Canvas;
        AutoScaleMode = AutoScaleMode.Dpi;
        Disposed += (_, _) => tips.Dispose();

        startWithWindows = new ToggleSwitch(text.T("Start with Windows")) { Enabled = StartWithWindows.Executable is not null };
        checkForUpdates = new ToggleSwitch(text.T("Check for new versions"));
        autoInstall = new ToggleSwitch(text.T("Install automatically when Update is clicked")) { Margin = new Padding(24, 4, 0, 4) };
        checkForUpdates.CheckedChanged += (_, _) => autoInstall.Enabled = checkForUpdates.Checked;
        testResult = Ui.Text("", Ui.Muted, maxWidth: ContentWidth - 40);
        testResult.Visible = false;

        primary = new EngineRow(text.T("Primary"), allowNone: false, text, this);
        fallback = new EngineRow(text.T("If it fails"), allowNone: true, text, this);
        primary.Test.Click += async (_, _) => await TestAsync(primary);
        fallback.Test.Click += async (_, _) => await TestAsync(fallback);
        primary.Changed += () => primary.ShowStatus(signIn);
        fallback.Changed += () => fallback.ShowStatus(signIn);

        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Canvas };
        var page = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 18, 20, 8),
            BackColor = Ui.Canvas,
        };
        scroller.Controls.Add(page);

        page.Controls.Add(Header(icon, changeLanguage));

        var engineCard = Card(text.T("Translation engine"),
            text.T("Uses the Claude Code or Codex you are already signed in to on this PC. No API key needed."));
        var engines = Grid(5);
        primary.AddTo(engines);
        fallback.AddTo(engines);
        engineCard.Controls.Add(engines);
        var accountRow = new FlowRow { Margin = new Padding(0, 8, 0, 0) };
        foreach (var provider in Providers)
        {
            var pill = new StatusPill();
            var action = new PillButton("", PillKind.Soft) { Visible = false, MinimumSize = new Size(0, 26), Padding = new Padding(10, 1, 10, 1), Margin = new Padding(0, 3, 14, 3), Font = Ui.Regular(9f) };
            action.Click += (_, _) => AccountAction(provider);
            accountRow.Controls.AddRange([pill, action]);
            accounts[provider] = (pill, action);
        }
        engineCard.Controls.Add(accountRow);
        engineCard.Controls.Add(testResult);
        page.Controls.Add(engineCard.Panel);

        var hotkeyCard = Card(text.T("Hotkeys and target languages"),
            text.T("Select text and press a hotkey: it is replaced with the translation in place."));
        var hotkeyGrid = Grid(4);
        for (int i = 0; i < AppSettings.PresetCount; i++)
        {
            var enabled = new ToggleSwitch("") { Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 10, 4) };
            var hotkey = new HotkeyBox(text);
            var field = new RoundedPanel(Color.White, Ui.Line, 10)
            {
                Size = new Size(170, 34),
                Padding = new Padding(10, 8, 10, 4),
                Margin = new Padding(0, 3, 10, 3),
                Anchor = AnchorStyles.Left,
            };
            field.Controls.Add(hotkey);
            var language = new PillDropdown(250);
            language.SetOptions(Languages.All.Select(item => ((object)item.Code, item.ToString())));
            enabled.CheckedChanged += (_, _) =>
            {
                language.Enabled = enabled.Checked;
                hotkey.ForeColor = enabled.Checked ? Ui.Ink : Ui.Gray;
            };
            hotkeyGrid.Controls.AddRange([enabled, field, Ui.Text("→", Ui.Muted), language]);
            presets.Add((enabled, hotkey, language));
        }
        hotkeyCard.Controls.Add(hotkeyGrid);
        var toneRow = new FlowRow { Margin = new Padding(0, 8, 0, 0) };
        tone.SetOptions(new (object, string)[] { ("natural", text.T("Match the original")), ("casual", text.T("Casual")), ("formal", text.T("Polite")) });
        toneRow.Controls.AddRange([Ui.Text(text.T("Tone"), Ui.Muted), tone]);
        hotkeyCard.Controls.Add(toneRow);
        page.Controls.Add(hotkeyCard.Panel);

        var appsCard = Card(text.T("Apps that translate the whole input box"),
            text.T("In these apps, pressing the hotkey with nothing selected translates the whole message. Drag-select first to translate only part of it."));
        appsCard.Controls.Add(appChips);
        page.Controls.Add(appsCard.Panel);

        var generalCard = Card(text.T("General"), null);
        var bubbleRow = new FlowRow { Margin = new Padding(0, 4, 0, 6) };
        bubbleTheme.SetOptions(new (object, string)[] { ("light", text.T("White background, black text")), ("dark", text.T("Black background, white text")) });
        bubbleRow.Controls.AddRange([Ui.Text(text.T("Read-only translation bubble"), Ui.Ink), bubbleTheme]);
        generalCard.Controls.Add(bubbleRow);
        generalCard.Controls.Add(startWithWindows);
        generalCard.Controls.Add(checkForUpdates);
        generalCard.Controls.Add(autoInstall);
        page.Controls.Add(generalCard.Panel);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.White };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Line);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var version = new LinkLabel
        {
            Text = "v" + UpdateChecker.Current.ToString(3) + " · GitHub",
            AutoSize = true,
            LinkColor = Ui.Muted,
            ActiveLinkColor = Ui.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        version.LinkClicked += (_, _) => Open("https://github.com/HSoo-Kim/inline-translator");
        var saveButton = new PillButton(text.T("Save"), PillKind.Primary) { MinimumSize = new Size(96, 36) };
        var cancelButton = new PillButton(text.T("Cancel"), PillKind.Secondary) { MinimumSize = new Size(88, 36), DialogResult = DialogResult.Cancel };
        saveButton.Click += (_, _) => Save();
        cancelButton.Click += (_, _) => Close();
        updateButton.Click += async (_, _) => await installUpdate();
        footer.Controls.AddRange([version, updateButton, cancelButton, saveButton]);
        footer.Layout += (_, _) => Ui.LayoutFooter(footer, version, [updateButton, cancelButton, saveButton]);
        Controls.Add(scroller);
        Controls.Add(footer);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadSettings(settings);
        SetUpdate(update);
        Shown += async (_, _) => await RefreshSignInAsync();

        var preferred = page.GetPreferredSize(Size.Empty);
        int maxHeight = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 60;
        ClientSize = new Size(preferred.Width + SystemInformation.VerticalScrollBarWidth, Math.Min(preferred.Height + footer.Height, maxHeight));
    }

    public static readonly IReadOnlyList<(string Code, string Name)> DisplayLanguages =
        [("", "System default"), ("ko", "한국어"), ("en", "English"), ("zh-CN", "简体中文"), ("ja", "日本語")];

    public static string LanguageLabel(UiText text) =>
        text.Language == "en" ? text.T("Display language") : text.T("Display language") + " (Language)";

    public void SetUpdate(ReleaseInfo? release)
    {
        updateButton.Visible = release is not null;
        if (release is not null) updateButton.Text = "↑ " + text.T("Update to {0}", release.Tag);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.RoundWindow(this);
    }

    private Control Header(Icon icon, Action<string> changeLanguage)
    {
        var header = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 14), BackColor = Ui.Canvas, MinimumSize = new Size(ContentWidth, 0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var logo = new PictureBox { Image = new Icon(icon, 40, 40).ToBitmap(), Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0), Anchor = AnchorStyles.Left };
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty, BackColor = Ui.Canvas };
        var title = Ui.Text("Inline Translator", Ui.Ink, Ui.Bold(14f));
        title.Margin = Padding.Empty;
        var subtitle = Ui.Text(text.T("Select text, press the hotkey, done."), Ui.Muted);
        subtitle.Margin = Padding.Empty;
        titles.Controls.AddRange([title, subtitle]);
        var languageBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty, BackColor = Ui.Canvas };
        var languageLabel = Ui.Text("🌐 " + LanguageLabel(text), Ui.Muted, Ui.Regular(8.5f));
        languageLabel.Margin = new Padding(2, 0, 0, 2);
        uiLanguage.SetOptions(DisplayLanguages.Select(language => ((object)language.Code, language.Code == "" ? text.T("System default") : language.Name)));
        uiLanguage.Margin = Padding.Empty;
        uiLanguage.SelectionChanged += () =>
        {
            string code = (string)uiLanguage.SelectedValue!;
            BeginInvoke(() => changeLanguage(code));
        };
        languageBox.Controls.AddRange([languageLabel, uiLanguage]);
        header.Controls.AddRange([logo, titles, languageBox]);
        return header;
    }

    private sealed record CardParts(RoundedPanel Panel, Control.ControlCollection Controls);

    private static CardParts Card(string title, string? description)
    {
        var card = new RoundedPanel(Ui.Card, Ui.Line, 14)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(ContentWidth, 0),
            Padding = new Padding(20, 16, 20, 18),
            Margin = new Padding(0, 0, 0, 14),
        };
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(card.Padding.Left, card.Padding.Top),
            BackColor = Ui.Card,
            Margin = Padding.Empty,
        };
        card.Controls.Add(body);
        var heading = Ui.Text(title, Ui.Ink, Ui.Bold(11f));
        heading.Margin = Padding.Empty;
        body.Controls.Add(heading);
        if (description is not null)
        {
            var note = Ui.Text(description, Ui.Muted, Ui.Regular(9f), ContentWidth - 40);
            note.Margin = new Padding(0, 3, 0, 6);
            body.Controls.Add(note);
        }
        return new CardParts(card, body.Controls);
    }

    private static TableLayoutPanel Grid(int columns)
    {
        var grid = new TableLayoutPanel { ColumnCount = columns, AutoSize = true, Margin = new Padding(0, 4, 0, 0), BackColor = Ui.Card };
        for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
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
            presets[i].Hotkey.SetHotkey(Hotkey.TryParse(preset.Hotkey, out var hotkey) ? hotkey : null);
            presets[i].Language.Select(Languages.Find(preset.TargetLanguage)?.Code ?? "en");
            presets[i].Language.Enabled = preset.Enabled;
            presets[i].Hotkey.ForeColor = preset.Enabled ? Ui.Ink : Ui.Gray;
        }
        tone.Select(settings.Tone);
        bubbleTheme.Select(settings.BubbleTheme);
        uiLanguage.Select(settings.UiLanguage);
        wholeInputApps.AddRange(settings.WholeInputApps);
        ShowApps();
        startWithWindows.Checked = StartWithWindows.IsEnabled;
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
            var chip = new Chip(app);
            chip.Removed += () =>
            {
                wholeInputApps.Remove(app);
                ShowApps();
            };
            appChips.Controls.Add(chip);
        }
        var add = new PillButton(text.T("+ Add apps"), PillKind.Soft) { MinimumSize = new Size(0, 28), Padding = new Padding(12, 1, 12, 1), Margin = new Padding(0, 3, 6, 3), Font = Ui.Regular(9f) };
        add.Click += (_, _) => PickApps();
        appChips.Controls.Add(add);
        appChips.ResumeLayout();
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
        Primary = primary.Get(),
        Fallback = fallback.Get(),
        Presets = presets.Select(row => new HotkeyPreset(row.Enabled.Checked, row.Hotkey.GetHotkey()?.ToString() ?? "",
            row.Language.SelectedValue as string ?? "en")).ToArray(),
        Tone = tone.SelectedValue as string ?? "natural",
        BubbleTheme = bubbleTheme.SelectedValue as string ?? "light",
        WholeInputApps = wholeInputApps.ToArray(),
        UiLanguage = uiLanguage.SelectedValue as string ?? "",
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
        var target = Languages.Find(presets[0].Language.SelectedValue as string) ?? Languages.All[0];
        string sample = target.Code == "ko" ? "Hi! What time is the meeting tomorrow? I might be a bit late." : "안녕하세요! 내일 회의 몇 시에 하나요? 조금 늦을 것 같아요.";
        var settings = Collect() with { Primary = engine, Fallback = new EngineChoice(Provider.None, "") };
        row.Test.Enabled = false;
        testResult.Visible = true;
        testResult.ForeColor = Ui.Muted;
        testResult.Text = text.T("Testing {0}…", engine.DisplayName);
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await new Translator(EngineCli.TranslateAsync).TranslateAsync(settings, target, sample, CancellationToken.None);
            testResult.ForeColor = Ui.Green;
            testResult.Text = $"✓ {engine.DisplayName} · {clock.Elapsed.TotalSeconds:0.0}s · {result.Text}";
        }
        catch (TranslationFailedException failure)
        {
            testResult.ForeColor = Ui.Red;
            testResult.Text = "✗ " + failure.Message;
            if (EngineCli.IsSignInError(failure.Primary)) await RefreshSignInAsync();
        }
        finally
        {
            if (!IsDisposed) row.Test.Enabled = true;
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
                    row.Pill.Show($"{name} · {text.T("Signed in")}", Ui.Green, Ui.GreenSoft, Color.FromArgb(21, 128, 61));
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
    }

    private void AccountAction(Provider provider)
    {
        try
        {
            if (signIn[provider] == SignIn.NotInstalled)
            {
                Open(EngineCli.InstallPage(provider));
                return;
            }
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

    private static Color StatusColor(SignIn? state) => state switch
    {
        SignIn.SignedIn => Ui.Green,
        SignIn.SignedOut => Ui.Red,
        _ => Ui.Gray,
    };

    private sealed class EngineRow
    {
        private readonly bool allowNone;
        private readonly UiText text;
        private readonly IWin32Window owner;
        private readonly Label dot = new() { Text = "●", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Ui.Gray, Margin = new Padding(0, 4, 6, 4), BackColor = Color.Transparent };
        private readonly Label label;
        private readonly PillDropdown provider = new(150);
        private readonly PillDropdown model = new(210);
        public PillButton Test { get; }

        public EngineRow(string title, bool allowNone, UiText text, IWin32Window owner)
        {
            this.allowNone = allowNone;
            this.text = text;
            this.owner = owner;
            label = Ui.Text(title, Ui.Ink);
            label.MinimumSize = new Size(70, 0);
            Test = new PillButton(text.T("Test"), PillKind.Secondary) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
            var providers = new List<(object, string)>();
            if (allowNone) providers.Add((Provider.None, text.T("None")));
            providers.Add((Provider.Claude, "Claude Code"));
            providers.Add((Provider.Codex, "Codex"));
            provider.SetOptions(providers);
            provider.SelectionChanged += () => FillModels(AppSettings.DefaultModel(SelectedProvider));
            model.AllowCustom(text.T("Enter a model name…"), () => InputDialog.Ask(owner, text.T("Model"),
                text.T("Type the exact model name, for example claude-opus-5-5."), text.T("OK"), text.T("Cancel")));
        }

        public event Action? Changed;

        private Provider SelectedProvider => provider.SelectedValue is Provider value ? value : Provider.Claude;

        public void AddTo(TableLayoutPanel grid) => grid.Controls.AddRange([dot, label, provider, model, Test]);

        public void Set(EngineChoice engine)
        {
            provider.Select(engine.Provider);
            FillModels(engine.Model);
        }

        public EngineChoice Get()
        {
            var selected = SelectedProvider;
            string value = model.SelectedValue as string ?? "";
            return new EngineChoice(selected, selected == Provider.None ? "" : AppSettings.IsValidModel(value) ? value : AppSettings.DefaultModel(selected));
        }

        public void ShowStatus(IReadOnlyDictionary<Provider, SignIn?> states)
        {
            var choice = SelectedProvider;
            dot.Visible = choice != Provider.None;
            if (choice != Provider.None) dot.ForeColor = StatusColor(states[choice]);
        }

        private void FillModels(string selected)
        {
            var choice = SelectedProvider;
            model.Enabled = Test.Enabled = choice != Provider.None;
            model.SetOptions(EngineCli.SuggestedModels(choice).Select(option => ((object)option.Id, option.Label)));
            if (choice != Provider.None && selected.Length > 0) model.Select(selected);
            else model.SelectIndex(-1);
            Changed?.Invoke();
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
        BorderStyle = BorderStyle.None;
        BackColor = Color.White;
        Dock = DockStyle.Fill;
        TextAlign = HorizontalAlignment.Center;
        Font = Ui.Bold(9.5f);
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
