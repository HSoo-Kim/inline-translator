using System.Diagnostics;

namespace InlineTranslator;

public static class ErrorLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly Lock Gate = new();

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InlineTranslator", "error.log");

    // Only our own context and the exception message are written: never the text being translated.
    public static void Write(string context, Exception error)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {context}: {error.GetType().Name}: {error.Message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes) file.Delete();
                File.AppendAllText(FilePath, line);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine("Error log unavailable: " + failure.Message + " / " + line);
            }
        }
    }
}
