using System.Diagnostics;
using InlineTranslator;

// Developer tool: UI commands run only on a private desktop. No simulated desktop input.
internal static class Program
{
    internal static readonly TimeSpan QaTimeout = TimeSpan.FromSeconds(30);

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is ["render", var folder])
            return OperationQa.RequirePrivateDesktop() ? RunUiAsync(() => RenderAsync(folder)) : 2;
        if (args is ["verify"])
            return OperationQa.RequirePrivateDesktop() ? RunUiAsync(OperationQa.VerifyAsync) : 2;
        if (args is ["lnk", var shortcut])
        {
            try { Console.WriteLine("target: " + AppCatalog.ShortcutTarget(shortcut)); }
            catch (Exception error) { Console.WriteLine(error); }
            return 0;
        }
        if (args is ["apps"])
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var apps = AppCatalog.Discover();
            Console.WriteLine($"{apps.Count} apps");
            foreach (var app in apps.Take(40)) Console.WriteLine($"{app.ProcessName} | {app.DisplayName} | running={app.Running}");
            return 0;
        }
        if (args is ["translate", var provider, var model, var language, var message]) return Translate(provider, model, language, message);
        Console.Error.WriteLine("usage: Preview render <folder> | Preview verify | Preview translate <Claude|Codex> <model> <language> <text>");
        return 2;
    }

    private static int RunUiAsync(Func<Task> operation)
    {
        using var context = new ApplicationContext();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        int exitCode = 1;
        dispatcher.BeginInvoke((Action)(async () =>
        {
            try
            {
                await operation();
                exitCode = 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
            }
            finally
            {
                context.ExitThread();
            }
        }));
        Application.Run(context);
        return exitCode;
    }

    internal static async Task ShowAsync(Form form)
    {
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => shown.TrySetResult();
        form.Shown += handler;
        try
        {
            form.Show();
            await shown.Task.WaitAsync(QaTimeout);
        }
        finally
        {
            form.Shown -= handler;
        }
    }

    internal static async Task FocusAsync(Control control)
    {
        if (control.Focused) return;
        var focused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => focused.TrySetResult();
        control.GotFocus += handler;
        try
        {
            if (!control.Focus()) throw new InvalidOperationException("Could not focus the private QA control.");
            await focused.Task.WaitAsync(QaTimeout);
        }
        finally
        {
            control.GotFocus -= handler;
        }
    }

    private static void Capture(Form form, string path)
    {
        form.Update();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path);
        Console.WriteLine($"{path} {form.Width}x{form.Height}");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static async Task RenderAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        string artifacts = Path.GetFullPath(Path.Combine(folder, "..", "..", "artifacts"));
        Directory.CreateDirectory(artifacts);
        foreach (string language in AppSettings.UiLanguages)
        {
            var settings = new AppSettings { UiLanguage = language }.Normalize();
            var text = new UiText(language);
            using var icon = AppIcon.Load();
            using var form = new SettingsForm(settings, text, icon, null, _ => [], () => Task.CompletedTask, _ => { });
            await ShowAsync(form);
            await form.InitialSignIn.WaitAsync(QaTimeout);
            form.PerformLayout();
            var scroller = form.Controls.OfType<Panel>().Single(panel => panel.AutoScroll);
            scroller.AutoScrollPosition = Point.Empty;
            Capture(form, Path.Combine(folder, $"settings-{language}.png"));

            // Include the reading shortcut even when it falls between the two viewport ends.
            var reading = Descendants(scroller).Single(control => control.Text == text.T("Read-only translation hotkey"));
            int readingY = scroller.PointToClient(reading.PointToScreen(Point.Empty)).Y;
            scroller.AutoScrollPosition = new Point(0, readingY + -scroller.AutoScrollPosition.Y - 12);
            Capture(form, Path.Combine(folder, $"settings-{language}-reading.png"));

            scroller.AutoScrollPosition = new Point(0, scroller.DisplayRectangle.Height);
            Capture(form, Path.Combine(folder, $"settings-{language}-end.png"));
            form.Close();
        }
        var korean = new UiText("ko");
        using (var busy = new BusyIndicator())
        {
            busy.ShowNear(new Point(200, 200), korean.T("Translating to {0}… · Esc to cancel", Languages.All[0].NativeName));
            Capture(busy, Path.Combine(artifacts, "busy-esc-ko.png"));
        }
        foreach (bool dark in new[] { false, true })
        {
            using var bubble = new TranslationBubble();
            bubble.ShowAt(new Rectangle(200, 200, 120, 20), "English · Claude · Haiku 5.5",
                "I'll send the meeting materials by 3 PM tomorrow.", korean.T("Translation · press any key or click to close"), dark);
            Capture(bubble, Path.Combine(artifacts, dark ? "bubble-dark.png" : "bubble-light.png"));
            bubble.ShowAt(new Rectangle(200, 200, 120, 20), "English · Claude · Haiku 5.5",
                "I'll send the meeting materials by 3 PM tomorrow.", korean.T("Input changed · translation shown without replacing text"), dark);
            Capture(bubble, Path.Combine(artifacts, dark ? "bubble-input-changed-dark.png" : "bubble-input-changed-light.png"));
        }
        using (var form = new Form { Text = "focus check", Size = new Size(400, 200) })
        {
            var editable = new TextBox { Text = "editable", Location = new Point(10, 10), Width = 300 };
            var locked = new TextBox { Text = "read only", ReadOnly = true, Location = new Point(10, 50), Width = 300 };
            form.Controls.AddRange([editable, locked]);
            await ShowAsync(form);
            await FocusAsync(editable);
            Console.WriteLine($"focus editable -> readOnly={FocusInspector.IsReadOnly(form.Handle)}");
            await FocusAsync(locked);
            Console.WriteLine($"focus read-only -> readOnly={FocusInspector.IsReadOnly(form.Handle)}");
            locked.Select(5, 4);
            var field = locked.RectangleToScreen(locked.ClientRectangle);
            var selected = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
            nint handle = form.Handle;
            var worker = new Thread(() =>
            {
                try
                {
                    selected.TrySetResult(SelectionLocator.Find(handle, error => Console.WriteLine("selection error: " + error), step => Console.WriteLine("selection step: " + step)));
                }
                catch (Exception error)
                {
                    selected.TrySetException(error);
                }
            }) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            Rectangle? found = await selected.Task.WaitAsync(QaTimeout);
            Console.WriteLine($"same-process selection rect={found?.ToString() ?? "none"} textbox={field}");
        }
        using (var picker = new AppPicker(new UiText("ko"), ["Telegram", "KakaoTalk"]))
        {
            await ShowAsync(picker);
            Capture(picker, Path.Combine(artifacts, "app-picker-ko.png"));
        }
    }

    private static int Translate(string provider, string model, string language, string message)
    {
        var engine = new EngineChoice(Enum.Parse<Provider>(provider), model);
        var settings = new AppSettings { Primary = engine, Fallback = new EngineChoice(Provider.None, "") }.Normalize();
        var home = Languages.Home(settings.HomeLanguage);
        var target0 = Languages.Find(language) ?? Languages.All[0];
        Console.WriteLine($"culture={System.Globalization.CultureInfo.CurrentUICulture.Name} home={home.Code} target={target0.Code} script={Direction.Dominant(message)} output={Direction.Resolve(message, target0, home)?.Code ?? "model decides"}");
        var target = Languages.Find(language) ?? throw new ArgumentException("Unknown language " + language);
        var clock = Stopwatch.StartNew();
        try
        {
            var result = new Translator(EngineCli.TranslateAsync).TranslateAsync(settings, target, message, CancellationToken.None)
                .GetAwaiter().GetResult();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine($"{engine.DisplayName} {clock.Elapsed.TotalSeconds:0.0}s: {result.Text}");
            return 0;
        }
        catch (TranslationFailedException failure)
        {
            Console.WriteLine($"{engine.DisplayName} FAILED {clock.Elapsed.TotalSeconds:0.0}s: {failure.Message}");
            return 1;
        }
    }
}
