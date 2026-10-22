using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace InlineTranslator;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string input,
        string workingDirectory, TimeSpan timeout, CancellationToken token)
    {
        var utf8 = new UTF8Encoding(false);
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + fileName);
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        try
        {
            try
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The CLI exited before reading its input (for example a bad flag); its exit code and stderr explain why.
            }
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            token.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"{Path.GetFileNameWithoutExtension(fileName)} did not answer within {timeout.TotalSeconds:0} seconds.");
        }
        return new ProcessResult(process.ExitCode, await output, await error);
    }
}

public sealed class EngineException(string message) : Exception(message);

public static class EngineCli
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static string ProductName(Provider provider) => provider == Provider.Codex ? "Codex" : "Claude Code";

    public static string? Resolve(Provider provider)
    {
        if (provider == Provider.None) return null;
        string name = provider == Provider.Claude ? "claude" : "codex";
        string[] path = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = path.Select(directory => Path.Combine(directory, name + ".exe"))
            .Concat(path.Select(directory => Path.Combine(directory, name + ".cmd")))
            .ToList();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidates.AddRange(provider == Provider.Claude
            ? [Path.Combine(home, ".local", "bin", "claude.exe"), Path.Combine(roaming, "npm", "claude.cmd")]
            : [Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe"), Path.Combine(roaming, "npm", "codex.cmd")]);
        return candidates.FirstOrDefault(File.Exists);
    }

    public static IReadOnlyList<string> ClaudeArguments(string model, string instructions) =>
    [
        "-p", "--model", model, "--system-prompt", instructions,
        "--safe-mode", "--strict-mcp-config", "--tools", "", "--no-session-persistence", "--effort", "low",
    ];

    public static IReadOnlyList<string> CodexArguments(string model, string workDirectory, string outputFile) =>
    [
        "exec", "--ephemeral", "--skip-git-repo-check", "--ignore-user-config", "--ignore-rules",
        "--sandbox", "read-only", "--cd", workDirectory, "--model", model,
        "--config", "model_reasoning_effort=low", "--output-last-message", outputFile, "-",
    ];

    public static string CodexInput(string instructions, string text) => instructions + "\n\nMessage:\n" + text;

    public static async Task<string> TranslateAsync(EngineChoice engine, string instructions, string text, CancellationToken token)
    {
        string product = ProductName(engine.Provider);
        string executable = Resolve(engine.Provider)
            ?? throw new EngineException($"{product} CLI was not found. Install it and sign in once.");
        string work = Directory.CreateTempSubdirectory("InlineTranslator-").FullName;
        try
        {
            if (engine.Provider == Provider.Claude)
            {
                var result = await ProcessRunner.RunAsync(executable, ClaudeArguments(engine.Model, instructions), text,
                    work, Timeout, token);
                if (result.ExitCode != 0) throw new EngineException(Describe(product, result.ExitCode, LastLine(result.Output + "\n" + result.Error)));
                return result.Output;
            }
            string outputFile = Path.Combine(work, "translation.txt");
            var codex = await ProcessRunner.RunAsync(executable, CodexArguments(engine.Model, work, outputFile),
                CodexInput(instructions, text), work, Timeout, token);
            // Codex echoes the conversation to stderr, so only its ERROR lines may be surfaced or logged.
            if (codex.ExitCode != 0 || !File.Exists(outputFile))
                throw new EngineException(Describe(product, codex.ExitCode, CodexError(codex.Error)));
            return await File.ReadAllTextAsync(outputFile, token);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write("Could not remove temporary folder", error);
            }
        }
    }

    public static string CodexError(string stderr)
    {
        string? line = stderr.Split('\n').Select(item => item.Trim()).LastOrDefault(item => item.StartsWith("ERROR", StringComparison.Ordinal));
        if (line is null) return "";
        int start = line.IndexOf("\"message\":\"", StringComparison.Ordinal);
        if (start < 0) return Truncate(line);
        start += "\"message\":\"".Length;
        int end = line.IndexOf('"', start);
        return Truncate(end > start ? line[start..end] : line[start..]);
    }

    public static IReadOnlyList<string> SuggestedModels(Provider provider) => provider switch
    {
        Provider.Claude => ["haiku", "sonnet", "opus", "claude-haiku-5-5"],
        Provider.Codex => CachedCodexModels() is { Count: > 0 } models ? models : [AppSettings.DefaultCodexModel],
        _ => [],
    };

    public static IReadOnlyList<string> ParseCodexModels(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return [];
            return models.EnumerateArray()
                .Where(model => model.ValueKind == JsonValueKind.Object
                    && (!model.TryGetProperty("visibility", out var visibility) || visibility.GetString() == "list"))
                .Select(model => model.TryGetProperty("slug", out var slug) ? slug.GetString() : null)
                .OfType<string>()
                .Where(AppSettings.IsValidModel)
                .Distinct()
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> CachedCodexModels()
    {
        string home = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        string cache = Path.Combine(home, "models_cache.json");
        try
        {
            return File.Exists(cache) ? ParseCodexModels(File.ReadAllText(cache)) : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Could not read the Codex model list", error);
            return [];
        }
    }

    private static string Describe(string product, int exitCode, string detail) =>
        detail.Length > 0 ? $"{product}: {detail}" : $"{product} exited with code {exitCode}.";

    private static string LastLine(string text) =>
        Truncate(text.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? "");

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
