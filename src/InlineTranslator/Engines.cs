using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

public enum SignIn { NotInstalled, SignedIn, SignedOut, Unknown }

public sealed record ModelOption(string Id, string Label)
{
    public override string ToString() => Label;

    public static string Resolve(IEnumerable<ModelOption> options, string text)
    {
        string value = text.Trim();
        return options.FirstOrDefault(option => option.Label.Equals(value, StringComparison.OrdinalIgnoreCase)
            || option.Id.Equals(value, StringComparison.OrdinalIgnoreCase))?.Id ?? value;
    }
}

public static class EngineCli
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static string ProductName(Provider provider) => provider == Provider.Codex ? "Codex" : "Claude Code";

    public static string InstallPage(Provider provider) => provider == Provider.Codex
        ? "https://github.com/openai/codex"
        : "https://docs.anthropic.com/en/docs/claude-code";

    private static readonly Regex SignInProblem = new(
        @"authenticat|not logged in|log ?in|sign ?in|oauth|\b401\b|unauthori[sz]ed|token (has )?expired|session expired",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsSignInError(Exception error) =>
        error is EngineException && !error.Message.Contains("was not found", StringComparison.Ordinal) && SignInProblem.IsMatch(error.Message);

    public static async Task<SignIn> CheckSignInAsync(Provider provider, CancellationToken token)
    {
        string? executable = Resolve(provider);
        if (executable is null) return SignIn.NotInstalled;
        try
        {
            var result = await ProcessRunner.RunAsync(executable,
                provider == Provider.Claude ? ["auth", "status"] : ["login", "status"], "", Path.GetTempPath(),
                TimeSpan.FromSeconds(20), token);
            return provider == Provider.Claude ? ParseClaudeSignIn(result) : ParseCodexSignIn(result);
        }
        catch (Exception error) when (error is TimeoutException or Win32Exception or IOException or InvalidOperationException)
        {
            ErrorLog.Write($"Could not check {ProductName(provider)} sign-in", error);
            return SignIn.Unknown;
        }
    }

    public static SignIn ParseClaudeSignIn(ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.TryGetProperty("loggedIn", out var loggedIn) && loggedIn.ValueKind == JsonValueKind.True
                ? SignIn.SignedIn
                : SignIn.SignedOut;
        }
        catch (JsonException)
        {
            return result.ExitCode == 0 ? SignIn.Unknown : SignIn.SignedOut;
        }
    }

    public static SignIn ParseCodexSignIn(ProcessResult result)
    {
        string text = result.Output + "\n" + result.Error;
        if (text.Contains("Not logged in", StringComparison.OrdinalIgnoreCase)) return SignIn.SignedOut;
        if (result.ExitCode == 0 && text.Contains("Logged in", StringComparison.OrdinalIgnoreCase)) return SignIn.SignedIn;
        return result.ExitCode == 0 ? SignIn.Unknown : SignIn.SignedOut;
    }

    public static string SignInCommandLine(string executable, Provider provider) =>
        $"/d /c \"\"{executable}\" {(provider == Provider.Claude ? "auth login" : "login")} & echo. & pause\"";

    // Opens a visible console because both CLIs sign in interactively through the browser.
    public static Process StartSignIn(Provider provider)
    {
        string executable = Resolve(provider)
            ?? throw new EngineException($"{ProductName(provider)} CLI was not found. Install it and sign in once.");
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), SignInCommandLine(executable, provider))
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        return Process.Start(info) ?? throw new InvalidOperationException("Could not open the sign-in window.");
    }

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

    public static readonly IReadOnlyList<ModelOption> ClaudeModels =
    [
        new("claude-haiku-5-5", "Haiku 5.5"),
        new("claude-sonnet-5-5", "Sonnet 5.5"),
        new("claude-opus-5-5", "Opus 5.5"),
        new("claude-fable-5", "Fable 5"),
    ];

    public static IReadOnlyList<ModelOption> SuggestedModels(Provider provider) => provider switch
    {
        Provider.Claude => ClaudeModels,
        Provider.Codex => CachedCodexModels() is { Count: > 0 } models ? models : [new(AppSettings.DefaultCodexModel, "GPT-5.6-Luna")],
        _ => [],
    };

    public static IReadOnlyList<ModelOption> ParseCodexModels(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return [];
            return models.EnumerateArray()
                .Where(model => model.ValueKind == JsonValueKind.Object
                    && (!model.TryGetProperty("visibility", out var visibility) || visibility.GetString() == "list"))
                .Select(model => (
                    Id: model.TryGetProperty("slug", out var slug) ? slug.GetString() : null,
                    Label: model.TryGetProperty("display_name", out var name) ? name.GetString() : null))
                .Where(model => AppSettings.IsValidModel(model.Id))
                .DistinctBy(model => model.Id)
                .Select(model => new ModelOption(model.Id!, string.IsNullOrWhiteSpace(model.Label) ? model.Id! : model.Label))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ModelOption> CachedCodexModels()
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
