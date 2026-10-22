namespace InlineTranslator;

internal sealed class AppPicker : Form
{
    private readonly UiText text;
    private readonly IReadOnlyList<AppEntry> apps;
    private readonly HashSet<string> chosen;
    private readonly TextBox search = new() { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Font = Ui.Regular(10f) };
    private readonly ListView list = new()
    {
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.None,
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        Font = Ui.Regular(),
        SmallImageList = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit },
    };
    private bool filling;

    public IReadOnlyList<string> Selected { get; private set; } = [];

    public AppPicker(UiText text, IEnumerable<string> current)
    {
        this.text = text;
        chosen = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        Cursor = Cursors.WaitCursor;
        var discovered = AppCatalog.Discover();
        apps = discovered
            .Concat(chosen.Where(name => !discovered.Any(app => app.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Select(name => new AppEntry(name, name, null, false)))
            .OrderByDescending(app => chosen.Contains(app.ProcessName))
            .ThenByDescending(app => AppCatalog.IsKnownChatApp(app.ProcessName))
            .ToList();
        Cursor = Cursors.Default;

        Text = text.T("Choose apps");
        ShowIcon = false;
        Font = Ui.Regular();
        BackColor = Ui.Canvas;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 560);
        Padding = new Padding(18);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Ui.Canvas };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var hint = Ui.Text(text.T("Pick the chat apps where pressing the hotkey without selecting should translate the whole message."), Ui.Muted, maxWidth: 440);
        hint.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(hint);

        var searchField = new RoundedPanel(Color.White, Ui.Line, 10) { Dock = DockStyle.Top, Height = 38, Padding = new Padding(12, 10, 12, 6), Margin = new Padding(0, 0, 0, 10) };
        searchField.Controls.Add(search);
        search.PlaceholderText = "🔍 " + text.T("Search apps");
        search.TextChanged += (_, _) => Fill();
        layout.Controls.Add(searchField);

        var listCard = new RoundedPanel(Color.White, Ui.Line, 14) { Dock = DockStyle.Fill, Padding = new Padding(8, 10, 8, 10), Margin = new Padding(0, 0, 0, 12) };
        list.Columns.Add("", 250);
        list.Columns.Add("", 150);
        list.Resize += (_, _) => list.Columns[1].Width = Math.Max(80, list.ClientSize.Width - list.Columns[0].Width - 2);
        listCard.Controls.Add(list);
        layout.Controls.Add(listCard);
        list.ItemChecked += (_, e) =>
        {
            if (filling) return;
            string name = (string)e.Item.Tag!;
            if (e.Item.Checked) chosen.Add(name);
            else chosen.Remove(name);
        };

        var buttons = new Panel { Dock = DockStyle.Fill, Height = 44, Margin = Padding.Empty, BackColor = Ui.Canvas };
        var ok = new PillButton(text.T("Done"), PillKind.Primary) { MinimumSize = new Size(96, 36), DialogResult = DialogResult.OK };
        var cancel = new PillButton(text.T("Cancel"), PillKind.Secondary) { MinimumSize = new Size(88, 36), DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([cancel, ok]);
        buttons.Layout += (_, _) => Ui.LayoutFooter(buttons, null, [cancel, ok]);
        layout.Controls.Add(buttons);
        layout.RowStyles[3] = new RowStyle(SizeType.Absolute, 48);
        AcceptButton = ok;
        CancelButton = cancel;
        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.OK)
                Selected = apps.Select(app => app.ProcessName).Where(chosen.Contains)
                    .Concat(chosen.Where(name => !apps.Any(app => app.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))))
                    .ToList();
        };
        Disposed += (_, _) => list.SmallImageList?.Dispose();
        ActiveControl = list;
        Fill();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.RoundWindow(this);
    }

    private void Fill()
    {
        filling = true;
        list.BeginUpdate();
        list.Items.Clear();
        string query = search.Text.Trim();
        foreach (var app in apps.Where(app => query.Length == 0
            || app.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || app.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var item = new ListViewItem(app.DisplayName) { Tag = app.ProcessName, Checked = chosen.Contains(app.ProcessName), ImageKey = IconKey(app) };
            var tags = new List<string> { app.ProcessName };
            if (AppCatalog.IsKnownChatApp(app.ProcessName)) tags.Add(text.T("Chat app"));
            if (app.Running) tags.Add(text.T("Running"));
            item.SubItems.Add(string.Join(" · ", tags));
            item.SubItems[1].ForeColor = Ui.Muted;
            item.UseItemStyleForSubItems = false;
            list.Items.Add(item);
        }
        list.EndUpdate();
        filling = false;
    }

    private string IconKey(AppEntry app)
    {
        var images = list.SmallImageList!;
        if (images.Images.ContainsKey(app.ProcessName)) return app.ProcessName;
        if (app.ExecutablePath is { } path)
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null) images.Images.Add(app.ProcessName, icon.ToBitmap());
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Some executables have no readable icon; the row still shows the name.
                return "";
            }
        }
        return images.Images.ContainsKey(app.ProcessName) ? app.ProcessName : "";
    }
}
