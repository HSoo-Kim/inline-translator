using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace InlineTranslator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string settingsPath = SettingsStore.DefaultPath;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--wait-pid" && i + 1 < args.Length) WaitForExit(args[++i]);
            else if (args[i] == "--settings" && i + 1 < args.Length) settingsPath = Path.GetFullPath(args[++i]);
        }

        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settingsPath.ToUpperInvariant())))[..16];
        uint activation = Native.RegisterWindowMessage("InlineTranslator.Activate." + key);
        using var mutex = new Mutex(true, @"Local\InlineTranslator-" + key, out bool firstInstance);
        if (!firstInstance)
        {
            // Launching the app again opens the running instance's settings instead of starting a second copy.
            Native.PostMessage(0xFFFF, activation, 0, 0);
            return;
        }

        UpdateInstaller.RemovePrevious(Environment.ProcessPath);
        var settings = SettingsStore.Load(settingsPath);
        bool firstRun = settings is null;
        settings ??= new AppSettings().Normalize();
        if (firstRun)
        {
            try
            {
                SettingsStore.Save(settingsPath, settings);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write("Could not create the settings file", error);
            }
        }
        Application.Run(new TrayApp(settingsPath, settings, firstRun, activation));
    }

    private static void WaitForExit(string processId)
    {
        if (!int.TryParse(processId, out int id)) return;
        try
        {
            using var previous = Process.GetProcessById(id);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // The previous version already exited.
        }
    }
}
