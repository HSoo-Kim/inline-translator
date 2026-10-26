namespace InlineTranslator;

internal static class ClipboardText
{
    public static string? Read(IDataObject? data)
    {
        if (data is null) return null;
        foreach (string format in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText })
            if (data.TryGetData<string>(format, autoConvert: true, out var text) && !string.IsNullOrWhiteSpace(text))
                return text;
        if (!data.TryGetData<string>(DataFormats.Rtf, autoConvert: false, out var rtf) || string.IsNullOrWhiteSpace(rtf))
            return null;
        try
        {
            // Parse locally, without exporting the RTF markup or embedded formatting to the engine.
            using var editor = new RichTextBox { DetectUrls = false, Rtf = rtf };
            return string.IsNullOrWhiteSpace(editor.Text) ? null : editor.Text;
        }
        catch (ArgumentException)
        {
            return null; // A producer can advertise RTF before providing valid RTF data.
        }
    }
}
