using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using InlineTranslator;

// Pure data-object tests: no system clipboard, user keystrokes or visible windows.
internal static class ClipboardQa
{
    internal static Task VerifyAsync()
    {
        var failures = new List<string>();
        void Check(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS clipboard/" + name); }
            catch (Exception error) { failures.Add(name); Console.WriteLine("FAIL clipboard/" + name + ": " + error.Message); }
        }
        void Equal(string? expected, string? actual)
        {
            if (expected != actual) throw new InvalidOperationException($"Expected <{expected}> but was <{actual}>.");
        }
        Check("unicode", () =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, false, "한글 🌍\r\nsecond\tcell");
            Equal("한글 🌍\r\nsecond\tcell", ClipboardText.Read(data));
        });
        Check("rtf-only", () =>
        {
            const string source = "한글 🌍\nsecond\tcell";
            using var editor = new RichTextBox { Text = source };
            var data = new DataObject();
            data.SetData(DataFormats.Rtf, false, editor.Rtf);
            Equal(source, ClipboardText.Read(data));
        });
        foreach (string format in new[] { DataFormats.Text, DataFormats.OemText })
            Check(format, () =>
            {
                var data = new DataObject();
                data.SetData(format, false, "plain ASCII");
                Equal("plain ASCII", ClipboardText.Read(data));
            });
        Check("plain-text-priority", () =>
        {
            using var editor = new RichTextBox { Text = "not selected" };
            var data = new DataObject();
            data.SetData(DataFormats.Rtf, false, editor.Rtf);
            data.SetData(DataFormats.UnicodeText, false, "selected text");
            Equal("selected text", ClipboardText.Read(data));
        });
        Check("invalid-rtf", () =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.Rtf, false, "invalid RTF");
            Equal(null, ClipboardText.Read(data));
        });
        Check("non-text", () =>
        {
            var data = new DataObject();
            data.SetData("OwnedFixtureFormat", false, new byte[] { 1, 2 });
            Equal(null, ClipboardText.Read(data));
        });
        Check("unreadable-snapshot", () =>
        {
            try { ClipboardSnapshot.Take(new UnreadableData()); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Unpreservable clipboard must stop capture before any copy.");
        });
        Check("empty-snapshot", () => ClipboardSnapshot.Take(new DataObject()));
        if (failures.Count != 0) throw new InvalidOperationException(string.Join(", ", failures));
        return Task.CompletedTask;
    }

    internal static Task VerifyNativeAsync()
    {
        if (!OperationQa.RequirePrivateDesktop()) throw new InvalidOperationException("Native clipboard QA requires its private desktop.");
        using var editor = new RichTextBox { Text = "KEEP|회의 자료 🌍|TAIL" };
        _ = editor.Handle;
        const string source = "회의 자료 🌍", translated = "Meeting materials 🌍";
        var snapshot = ClipboardSnapshot.Take();
        uint owned = Native.GetClipboardSequenceNumber();
        try
        {
            editor.Select(5, source.Length);
            editor.Copy(); // Native RichEdit copy, never a global keystroke.
            owned = Native.GetClipboardSequenceNumber();
            if (ClipboardText.Read(Clipboard.GetDataObject()) != source)
                throw new InvalidOperationException("Native RichEdit selection capture failed.");
            Console.WriteLine("PASS clipboard/native-selection");
            var rich = new DataObject();
            rich.SetData(DataFormats.Rtf, false, editor.SelectedRtf);
            Clipboard.SetDataObject(rich, copy: true);
            owned = Native.GetClipboardSequenceNumber();
            if (ClipboardText.Read(Clipboard.GetDataObject()) != source)
                throw new InvalidOperationException("Native RTF-only clipboard capture failed.");
            Console.WriteLine("PASS clipboard/native-rtf-only");
            PrivateClipboard.SetText(translated);
            owned = Native.GetClipboardSequenceNumber();
            editor.Paste();
            if (editor.Text != "KEEP|" + translated + "|TAIL")
                throw new InvalidOperationException("Native paste must replace only the selection.");
            Console.WriteLine("PASS clipboard/native-selected-paste");
            editor.ReadOnly = true;
            editor.Select(5, translated.Length);
            PrivateClipboard.SetText("SHOULD NOT REPLACE THE READ-ONLY SELECTION");
            owned = Native.GetClipboardSequenceNumber();
            editor.Paste();
            if (editor.Text != "KEEP|" + translated + "|TAIL")
                throw new InvalidOperationException("Read-only original was modified.");
            Console.WriteLine("PASS clipboard/native-readonly-preserved");
        }
        finally
        {
            if (Native.GetClipboardSequenceNumber() == owned) snapshot.Restore();
        }
        return Task.CompletedTask;
    }

    private sealed class UnreadableData : ITypedDataObject
    {
        public object? GetData(string format, bool autoConvert) => throw new ExternalException("Fixture data is unavailable.");
        public object? GetData(string format) => GetData(format, false);
        public object? GetData(Type format) => GetData(format.FullName!, false);
        public bool GetDataPresent(string format, bool autoConvert) => format == "UnreadableFixture";
        public bool GetDataPresent(string format) => GetDataPresent(format, false);
        public bool GetDataPresent(Type format) => false;
        public string[] GetFormats(bool autoConvert) => ["UnreadableFixture"];
        public string[] GetFormats() => GetFormats(false);
        public void SetData(string format, bool autoConvert, object? data) => throw new NotSupportedException();
        public void SetData(string format, object? data) => throw new NotSupportedException();
        public void SetData(Type format, object? data) => throw new NotSupportedException();
        public void SetData(object? data) => throw new NotSupportedException();
        public bool TryGetData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
            [NotNullWhen(true), MaybeNullWhen(false)] out T data) => throw new ExternalException();
        public bool TryGetData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
            string format, [NotNullWhen(true), MaybeNullWhen(false)] out T data) => throw new ExternalException();
        public bool TryGetData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
            string format, bool autoConvert, [NotNullWhen(true), MaybeNullWhen(false)] out T data) => throw new ExternalException();
        public bool TryGetData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
            string format, Func<TypeName, Type?> resolver, bool autoConvert,
            [NotNullWhen(true), MaybeNullWhen(false)] out T data) => throw new ExternalException();
    }
}
