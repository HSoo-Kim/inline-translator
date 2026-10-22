using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace InlineTranslator;

public sealed record AppEntry(string ProcessName, string DisplayName, string? ExecutablePath, bool Running);

public static class AppCatalog
{
    private static readonly string[] Noise = ["uninstall", "unins", "setup", "updater", "readme", "help", "제거", "설치"];

    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "SystemSettings", "ApplicationFrameHost", "TextInputHost", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "LockApp", "InlineTranslator",
    };

    private static readonly HashSet<string> ChatApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Telegram", "KakaoTalk", "slack", "Discord", "WhatsApp", "LINE", "ms-teams", "Teams", "Signal",
        "WeChat", "Weixin", "QQ", "Messenger", "Zalo", "Viber", "Element", "Mattermost", "Zoom", "NateOn", "Jandi",
    };

    public static bool IsKnownChatApp(string processName) => ChatApps.Contains(AppSettings.NormalizeAppName(processName));

    public static bool IsUsefulShortcut(string displayName, string targetPath) =>
        targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !Noise.Any(word => displayName.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(targetPath).Contains(word, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<AppEntry> Merge(IEnumerable<AppEntry> installed, IEnumerable<AppEntry> running)
    {
        var apps = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in installed)
        {
            string name = AppSettings.NormalizeAppName(app.ProcessName);
            if (name.Length == 0) continue;
            if (!apps.TryGetValue(name, out var known) || app.DisplayName.Length < known.DisplayName.Length)
                apps[name] = app with { ProcessName = name };
        }
        foreach (var app in running)
        {
            string name = AppSettings.NormalizeAppName(app.ProcessName);
            if (name.Length == 0 || SystemProcesses.Contains(name)) continue;
            apps[name] = apps.TryGetValue(name, out var known)
                ? known with { Running = true, ExecutablePath = known.ExecutablePath ?? app.ExecutablePath }
                : app with { ProcessName = name, Running = true };
        }
        return apps.Values
            .OrderByDescending(app => app.Running)
            .ThenBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<AppEntry> Discover() => Merge(InstalledApps(), RunningApps());

    private static IEnumerable<AppEntry> InstalledApps()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        ];
        foreach (string root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(root, "*.lnk",
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write("Could not list Start menu shortcuts", error);
                continue;
            }
            foreach (string shortcut in shortcuts)
            {
                string? target = ShortcutTarget(shortcut);
                string name = Path.GetFileNameWithoutExtension(shortcut);
                if (target is null || !IsUsefulShortcut(name, target) || !File.Exists(target)) continue;
                yield return new AppEntry(Path.GetFileNameWithoutExtension(target), name, target, false);
            }
        }
    }

    private static IEnumerable<AppEntry> RunningApps()
    {
        var apps = new List<AppEntry>();
        int self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == 0) continue;
                    string? path = null;
                    string description = "";
                    try
                    {
                        path = process.MainModule?.FileName;
                        if (path is not null) description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? "";
                    }
                    catch (Win32Exception)
                    {
                        // Elevated or protected processes hide their path; the name alone still identifies them.
                    }
                    apps.Add(new AppEntry(process.ProcessName, description.Length > 0 ? description : process.ProcessName, path, true));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the list was being read.
                }
            }
        }
        return apps;
    }

    internal static string? ShortcutTarget(string shortcut)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(shortcut, 0);
            var buffer = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, 0, 0);
            string path = Environment.ExpandEnvironmentVariables(buffer.ToString());
            return path.Length > 0 ? path : null;
        }
        catch (Exception error) when (error is COMException or InvalidCastException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, nint findData, uint flags);
    }
}
