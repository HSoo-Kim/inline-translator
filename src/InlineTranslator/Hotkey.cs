namespace InlineTranslator;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

// Values match the Win32 RegisterHotKey modifier flags and virtual-key codes.
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<uint, string> Names = BuildNames();
    private static readonly Dictionary<string, uint> Codes =
        Names.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public bool IsValid =>
        Names.ContainsKey(VirtualKey) && (Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    public static bool IsSupportedKey(uint virtualKey) => Names.ContainsKey(virtualKey);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = HotkeyModifiers.None;
        uint? key = null;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            HotkeyModifiers? modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" => HotkeyModifiers.Win,
                _ => null,
            };
            if (modifier is { } flag)
            {
                if ((modifiers & flag) != 0) return false;
                modifiers |= flag;
            }
            else if (key is null && Codes.TryGetValue(part, out uint code)) key = code;
            else return false;
        }
        if (key is null) return false;
        hotkey = new Hotkey(modifiers, key.Value);
        return hotkey.IsValid;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (Names.TryGetValue(VirtualKey, out string? name)) parts.Add(name);
        return string.Join("+", parts);
    }

    private static Dictionary<uint, string> BuildNames()
    {
        var names = new Dictionary<uint, string>();
        for (char letter = 'A'; letter <= 'Z'; letter++) names[letter] = letter.ToString();
        for (char digit = '0'; digit <= '9'; digit++) names[digit] = digit.ToString();
        for (uint function = 1; function <= 24; function++) names[0x6F + function] = "F" + function;
        names[0x20] = "Space";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x25] = "Left";
        names[0x26] = "Up";
        names[0x27] = "Right";
        names[0x28] = "Down";
        names[0x2D] = "Insert";
        return names;
    }
}
