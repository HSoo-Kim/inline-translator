using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using InlineTranslator;

internal static class RecoveryQa
{
    private static readonly string Artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
    private static readonly string InstalledExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "InlineTranslator", UpdateChecker.ExecutableName);

    internal static async Task VerifyAsync(string folder)
    {
        if (!OperationQa.RequirePrivateDesktop())
            throw new InvalidOperationException("Replacement QA requires the matching private desktop.");

        string root = Path.GetFullPath(folder);
        Check(root.Equals(Artifacts, StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(Artifacts + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Replacement fixtures must be inside the repository's artifacts folder.");
        string fixture = Path.Combine(root, "replacement-" + Guid.NewGuid().ToString("N"));
        string exe = Path.Combine(fixture, UpdateChecker.ExecutableName);
        string settingsPath = Path.Combine(fixture, "settings.json");
        string package = Path.Combine(Artifacts, UpdateChecker.PackageName);
        for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
            if (directory.Exists)
                Check((directory.Attributes & FileAttributes.ReparsePoint) == 0, "Fixture ancestors must not redirect writes.");
        Directory.CreateDirectory(fixture);

        var settings = new AppSettings
        {
            Presets = AppSettings.DefaultPresets.Select(preset => preset with { Enabled = false }).ToArray(),
            ReadingHotkeyEnabled = false,
            CheckForUpdates = false,
            AutoInstallUpdates = false,
        }.Normalize();
        SettingsStore.Save(settingsPath, settings);
        byte[] settingsBytes = await File.ReadAllBytesAsync(settingsPath);
        File.Copy(InstalledExe, exe, overwrite: false);
        Version sourceVersion = ProductVersion(exe);
        Check(sourceVersion <= UpdateChecker.Current, "Copied source must not be newer than the built package.");
        string originalHash = await HashAsync(exe);
        string packageHash = await HashAsync(package);
        string expectedHash;
        using (var zip = ZipFile.OpenRead(package))
        {
            var entry = zip.GetEntry(UpdateChecker.ExecutableName)
                ?? throw new InvalidDataException("Built package is missing its executable.");
            await using var stream = entry.Open();
            expectedHash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        }
        var release = new ReleaseInfo(UpdateChecker.Current, "v" + UpdateChecker.Current.ToString(3),
            "https://github.com/HSoo-Kim/inline-translator/releases",
            new Uri("https://github.com/HSoo-Kim/inline-translator/releases/download/qa/" + UpdateChecker.PackageName),
            packageHash);

        Process? previous = null, replacement = null;
        uint previousThread = 0, replacementThread = 0;
        try
        {
            (previous, previousThread) = Start(exe, settingsPath);
            await ReadyAsync(previous, exe);
            Check(ProductVersion(exe) == sourceVersion, "Initial fixture must retain its copied source version.");
            using var lifetime = new CancellationTokenSource();
            Task previousExit = previous.WaitForExitAsync(lifetime.Token);
            try
            {
                int downloads = 0;
                using var installTimeout = new CancellationTokenSource(Program.QaTimeout);
                await UpdateInstaller.InstallAsync(release, exe, (url, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Check(url == release.PackageUrl, "Installer must request the fixture release URL.");
                    downloads++;
                    return Task.FromResult<Stream>(File.OpenRead(package));
                }, installTimeout.Token);
                Check(downloads == 1, "Installer must consume the authentic local package exactly once.");
                Check(!previousExit.IsCompleted && !previous.HasExited, "Old mapped process must survive executable replacement.");
                AssertOwned(previous, exe);
                Check(await HashAsync(exe) == expectedHash, "Installed executable must exactly match the package entry.");
                Check(await HashAsync(exe + ".old") == originalHash, "Previous image must be retained intact while mapped.");
                Check(!File.Exists(exe + ".download") && !File.Exists(exe + ".new"), "Installer must clean download and staging files.");
                Check(ProductVersion(exe) == release.Version, "Replacement product version must match the release.");
                Check(!previousExit.IsCompleted, "Old fixture must remain running until its deliberate graceful exit.");
                await StopAsync(previous, previousThread, exe, failSafe: false);
                await previousExit.WaitAsync(Program.QaTimeout);
                Check(previous.ExitCode == 0, "Old fixture must exit gracefully, not be terminated.");
            }
            finally
            {
                lifetime.Cancel();
            }

            Check(File.Exists(exe + ".old"), "Previous image must still exist before replacement startup.");
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new FileSystemWatcher(fixture, Path.GetFileName(exe) + ".old")
            {
                NotifyFilter = NotifyFilters.FileName,
            };
            watcher.Deleted += (_, _) => removed.TrySetResult();
            watcher.Error += (_, error) => removed.TrySetException(error.GetException());
            watcher.EnableRaisingEvents = true;
            (replacement, replacementThread) = Start(exe, settingsPath);
            await ReadyAsync(replacement, exe);
            await removed.Task.WaitAsync(Program.QaTimeout);
            Check(!File.Exists(exe + ".old"), "Replacement startup must remove the previous image.");
            Check(ProductVersion(replacement.MainModule?.FileName
                ?? throw new InvalidOperationException("Replacement has no mapped main module.")) == release.Version,
                "Running replacement must report the actual release product version.");
            byte[] restartedSettings = await File.ReadAllBytesAsync(settingsPath);
            Check(settingsBytes.SequenceEqual(restartedSettings), "Fixture settings must remain unchanged.");
            await StopAsync(replacement, replacementThread, exe, failSafe: false);
            Check(replacement.ExitCode == 0, "Replacement must also exit gracefully.");
            Console.WriteLine($"PASS update replacement {sourceVersion} -> {release.Version}: package SHA256={packageHash}; running old image survived; graceful WM_QUIT; new product version; .download/.new/.old cleanup. Fixture: {fixture}");
        }
        finally
        {
            try
            {
                if (replacement is not null)
                {
                    try { await StopAsync(replacement, replacementThread, exe, failSafe: true); }
                    finally { replacement.Dispose(); }
                }
            }
            finally
            {
                if (previous is not null)
                {
                    try { await StopAsync(previous, previousThread, exe, failSafe: true); }
                    finally { previous.Dispose(); }
                }
            }
        }
    }

    private static (Process Process, uint Thread) Start(string exe, string settings)
    {
        Check(OperationQa.RequirePrivateDesktop(), "Fixture launch lost its private desktop.");
        string desktop = Environment.GetEnvironmentVariable("INLINETRANSLATOR_QA_DESKTOP")!;
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>(), Desktop = @"WinSta0\" + desktop };
        var command = new StringBuilder($"\"{exe}\" --settings \"{settings}\"");
        if (!CreateProcessW(exe, command, 0, 0, false, 0x00000004, 0, Path.GetDirectoryName(exe), ref startup, out var created))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create suspended private fixture.");
        Process? process = null;
        try
        {
            process = Process.GetProcessById(checked((int)created.ProcessId));
            AssertOwned(process, exe);
            if (ResumeThread(created.Thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resume private fixture.");
            return (process, created.ThreadId);
        }
        catch
        {
            AssertOwned(created.Process, exe);
            if (!TerminateProcess(created.Process, 125))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not terminate failed fixture launch.");
            Check(WaitForSingleObject(created.Process, checked((uint)Program.QaTimeout.TotalMilliseconds)) == 0,
                "Failed fixture launch did not terminate within its timeout.");
            process?.Dispose();
            throw;
        }
        finally
        {
            CloseHandle(created.Thread);
            CloseHandle(created.Process);
        }
    }

    private static async Task ReadyAsync(Process process, string exe)
    {
        Check(await Task.Run(() => process.WaitForInputIdle(checked((int)Program.QaTimeout.TotalMilliseconds))),
            "Fixture did not reach input idle before the startup timeout.");
        Check(!process.HasExited, "Fixture exited during startup.");
        AssertOwned(process, exe);
    }

    private static async Task StopAsync(Process process, uint thread, string exe, bool failSafe)
    {
        if (process.HasExited) return;
        AssertOwned(process, exe);
        try
        {
            using var timeout = new CancellationTokenSource(Program.QaTimeout);
            Task exited = process.WaitForExitAsync(timeout.Token);
            if (!PostThreadMessageW(thread, 0x0012, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not post WM_QUIT to the fixture's primary thread.");
            await exited;
        }
        catch (Exception error) when (failSafe && error is Win32Exception or OperationCanceledException)
        {
            if (process.HasExited) return;
            AssertOwned(process, exe);
            using var timeout = new CancellationTokenSource(Program.QaTimeout);
            Task exited = process.WaitForExitAsync(timeout.Token);
            process.Kill(entireProcessTree: false);
            await exited;
            Console.WriteLine($"LIMIT fixture cleanup required fail-safe termination of owned PID {process.Id}.");
        }
    }

    private static void AssertOwned(Process process, string exe) => AssertOwned(process.Handle, exe);

    private static void AssertOwned(nint process, string exe)
    {
        var image = new StringBuilder(32_768);
        uint length = (uint)image.Capacity;
        if (!QueryFullProcessImageNameW(process, 0, image, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not establish fixture image ownership.");
        string actual = Path.GetFullPath(image.ToString());
        // Windows can report the original name or the renamed mapped image after replacement.
        Check(actual.Equals(exe, StringComparison.OrdinalIgnoreCase)
            || actual.Equals(exe + ".old", StringComparison.OrdinalIgnoreCase),
            "Refusing to stop a process outside the exact fixture image paths: " + actual);
    }

    private static Version ProductVersion(string path)
    {
        string value = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
        Check(Version.TryParse(value.Split('+')[0], out var version), "Executable has no valid product version: " + path);
        return UpdateChecker.Normalize(version!);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Desktop;
        public nint Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Size;
        public nint Reserved2, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, nint processSecurity, nint threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder image, ref uint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);
}
