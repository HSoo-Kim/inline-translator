using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace InlineTranslator;

public sealed record ReleaseInfo(Version Version, string Tag, string Url, Uri? PackageUrl = null, string? PackageSha256 = null);

public static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/HSoo-Kim/inline-translator/releases/latest";
    public const string PackageName = "inline-translator-win-x64.zip";
    public const string ExecutableName = "InlineTranslator.exe";
    public const int CheckIntervalMilliseconds = 5 * 60 * 1000;

    public static Version Current => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    // Reads only the latest release's tag, page and package digest; nothing about the user or their text is sent.
    public static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("InlineTranslator/" + Current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        await using var stream = await http.GetStreamAsync(LatestReleaseApi, token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        return Parse(json.RootElement);
    }

    public static ReleaseInfo? Parse(JsonElement release)
    {
        string tag = release.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() ?? "" : "";
        string url = release.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || !url.StartsWith("https://github.com/", StringComparison.Ordinal))
            return null;
        Uri? package = null;
        string? sha256 = null;
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name) || name.GetString() != PackageName) continue;
                string download = asset.TryGetProperty("browser_download_url", out var link) ? link.GetString() ?? "" : "";
                string digest = asset.TryGetProperty("digest", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() ?? "" : "";
                if (download.StartsWith("https://github.com/", StringComparison.Ordinal)
                    && digest.StartsWith("sha256:", StringComparison.Ordinal) && digest.Length == 7 + 64)
                {
                    package = new Uri(download);
                    sha256 = digest[7..];
                }
                break;
            }
        return new ReleaseInfo(Normalize(version), tag, url, package, sha256);
    }
}

// Replaces the running executable with the one in a verified release package. Windows lets a running
// executable be renamed, so the current file moves aside to "<exe>.old" and is deleted on the next start.
public static class UpdateInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("InlineTranslator/" + UpdateChecker.Current);
        return http;
    }

    public static async Task<Stream> DownloadAsync(Uri url, CancellationToken token)
    {
        var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(token);
    }

    public static string? InstallablePath()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return null;
        string probe = Path.Combine(Path.GetDirectoryName(exe)!, ".inline-translator-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return exe;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void RemovePrevious(string? exe)
    {
        if (exe is null || !File.Exists(exe + ".old")) return;
        try { File.Delete(exe + ".old"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Could not remove the previous version", error);
        }
    }

    public static async Task InstallAsync(ReleaseInfo release, string exe,
        Func<Uri, CancellationToken, Task<Stream>> download, CancellationToken token)
    {
        if (release.PackageUrl is null || release.PackageSha256 is null)
            throw new InvalidDataException("The release has no verifiable Windows package.");
        string package = exe + ".download", staged = exe + ".new", previous = exe + ".old";
        try
        {
            await using (var source = await download(release.PackageUrl, token))
            await using (var file = File.Create(package))
                await source.CopyToAsync(file, token);
            string actual;
            await using (var file = File.OpenRead(package))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!actual.Equals(release.PackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded package failed its SHA-256 check.");
            using (var zip = ZipFile.OpenRead(package))
            {
                var entry = zip.GetEntry(UpdateChecker.ExecutableName)
                    ?? throw new InvalidDataException("The package does not contain " + UpdateChecker.ExecutableName + ".");
                entry.ExtractToFile(staged, overwrite: true);
            }
            File.Move(exe, previous, overwrite: true);
            try { File.Move(staged, exe); }
            catch
            {
                File.Move(previous, exe);
                throw;
            }
        }
        finally
        {
            TryDelete(package);
            TryDelete(staged);
        }
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Could not remove a temporary update file", error);
        }
    }
}
