using InlineTranslator;

internal static class WindowQa
{
    internal static async Task VerifyAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (string language in AppSettings.UiLanguages)
        {
            var settings = new AppSettings { UiLanguage = language }.Normalize();
            var text = new UiText(language);
            using var icon = AppIcon.Load();
            using var form = new SettingsForm(settings, text, icon, null, _ => [], () => Task.CompletedTask, _ => { });
            await Program.ShowAsync(form);
            await form.InitialSignIn.WaitAsync(Program.QaTimeout);
            var area = Screen.FromControl(form).WorkingArea;
            Console.WriteLine($"dpi={form.DeviceDpi} style={form.FormBorderStyle} bounds={form.Bounds} work={area}");
            Check(form.FormBorderStyle == FormBorderStyle.Sizable && form.MaximizeBox, "Settings must allow resizing and maximizing.");
            Check(area.Contains(form.Bounds), "Shown outer window including title bar must fit the working area.");
            var scroller = form.Controls.OfType<Panel>().Single(panel => panel.AutoScroll);
            var save = form.AcceptButton as Control ?? throw new InvalidOperationException("Missing Save button.");
            var cancel = form.CancelButton as Control ?? throw new InvalidOperationException("Missing Cancel button.");
            foreach (var viewport in new[] { new Size(1280, 720), new Size(1024, 768), new Size(800, 600), new Size(640, 480) })
            {
                var work = new Rectangle(50, 40, viewport.Width, viewport.Height);
                form.FitToWorkingArea(work);
                Check(work.Contains(form.Bounds), "Window must fit reduced remote working area: " + viewport);
                form.Size = new Size(Math.Min(viewport.Width - 48, 720), Math.Min(viewport.Height - 48, 500));
                form.PerformLayout();
                Check(work.Contains(form.Bounds), "User-resized window must remain inside remote work area.");
                foreach (var button in new[] { save, cancel })
                {
                    var box = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                    Check(form.ClientRectangle.Contains(box), button.Text + " must remain inside the client area.");
                    Check(!scroller.Bounds.IntersectsWith(box), "Scrollable content must not cover " + button.Text);
                }
                form.SetUpdate(new ReleaseInfo(new Version(0, 1, 9), "v0.1.9", "https://github.com/HSoo-Kim/inline-translator"));
                form.PerformLayout();
                var footer = save.Parent!;
                var buttons = footer.Controls.OfType<Button>().Where(button => button.Visible).ToArray();
                for (int i = 0; i < buttons.Length; i++)
                {
                    Check(footer.ClientRectangle.Contains(buttons[i].Bounds), "Footer action must remain visible: " + buttons[i].Text);
                    for (int j = i + 1; j < buttons.Length; j++)
                        Check(!buttons[i].Bounds.IntersectsWith(buttons[j].Bounds), "Footer actions must not overlap.");
                }
                scroller.AutoScrollPosition = Point.Empty;
                Capture(form, Path.Combine(folder, $"settings-{language}-{viewport.Width}x{viewport.Height}-top.png"));
                scroller.AutoScrollPosition = new Point(0, scroller.DisplayRectangle.Height);
                Check(scroller.VerticalScroll.Value > 0, "Small remote window must scroll to remaining settings.");
                Capture(form, Path.Combine(folder, $"settings-{language}-{viewport.Width}x{viewport.Height}-end.png"));
                Console.WriteLine($"PASS {language} {viewport}: bounds={form.Bounds}, save/cancel visible, scroll={scroller.AutoScrollPosition}");
                form.SetUpdate(null);
            }
            var leftMonitor = new Rectangle(-1280, 0, 1280, 720);
            form.FitToWorkingArea(leftMonitor);
            Check(leftMonitor.Contains(form.Bounds), "Reduced remote desktop may have a negative monitor origin.");
        }
    }

    private static void Capture(Form form, string path)
    {
        form.Update();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path);
        Console.WriteLine(path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
