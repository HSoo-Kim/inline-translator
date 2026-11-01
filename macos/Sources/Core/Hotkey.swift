import Foundation

struct HotkeyModifiers: OptionSet, Hashable, Sendable {
    let rawValue: UInt32
    static let control = HotkeyModifiers(rawValue: 1)
    static let option = HotkeyModifiers(rawValue: 2)
    static let shift = HotkeyModifiers(rawValue: 4)
    static let command = HotkeyModifiers(rawValue: 8)

    /// Carbon `RegisterEventHotKey` modifier flags (cmdKey, shiftKey, optionKey, controlKey).
    var carbonFlags: UInt32 {
        (contains(.command) ? 0x0100 : 0) | (contains(.shift) ? 0x0200 : 0)
            | (contains(.option) ? 0x0800 : 0) | (contains(.control) ? 0x1000 : 0)
    }
}

/// A global shortcut. Key codes are Carbon virtual key codes (kVK_*), which name physical key positions.
struct Hotkey: Hashable, Sendable, CustomStringConvertible {
    let modifiers: HotkeyModifiers
    let keyCode: UInt32

    var isValid: Bool {
        Hotkey.names[keyCode] != nil && !modifiers.isDisjoint(with: [.control, .option, .command])
    }

    static func isSupportedKey(_ keyCode: UInt32) -> Bool { names[keyCode] != nil }

    static func parse(_ text: String?) -> Hotkey? {
        guard let text, !text.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
        var modifiers: HotkeyModifiers = []
        var key: UInt32?
        for raw in text.split(separator: "+", omittingEmptySubsequences: false) {
            let part = raw.trimmingCharacters(in: .whitespaces)
            let modifier: HotkeyModifiers? = switch part.uppercased() {
            case "CTRL", "CONTROL", "⌃": .control
            case "OPTION", "OPT", "ALT", "⌥": .option
            case "SHIFT", "⇧": .shift
            case "CMD", "COMMAND", "⌘": .command
            default: nil
            }
            if let modifier {
                if modifiers.contains(modifier) { return nil }
                modifiers.insert(modifier)
            } else if key == nil, let code = codes[part.uppercased()] {
                key = code
            } else {
                return nil
            }
        }
        guard let key else { return nil }
        let hotkey = Hotkey(modifiers: modifiers, keyCode: key)
        return hotkey.isValid ? hotkey : nil
    }

    /// Stored form, for example "Ctrl+Option+T".
    var description: String {
        var parts: [String] = []
        if modifiers.contains(.control) { parts.append("Ctrl") }
        if modifiers.contains(.option) { parts.append("Option") }
        if modifiers.contains(.shift) { parts.append("Shift") }
        if modifiers.contains(.command) { parts.append("Cmd") }
        if let name = Hotkey.names[keyCode] { parts.append(name) }
        return parts.joined(separator: "+")
    }

    /// Menu style form, for example "⌃⌥T".
    var symbols: String {
        var text = ""
        if modifiers.contains(.control) { text += "⌃" }
        if modifiers.contains(.option) { text += "⌥" }
        if modifiers.contains(.shift) { text += "⇧" }
        if modifiers.contains(.command) { text += "⌘" }
        return text + (Hotkey.symbolNames[keyCode] ?? Hotkey.names[keyCode] ?? "?")
    }

    /// Displays a stored hotkey string with symbols, keeping unparsable drafts readable.
    static func display(_ text: String) -> String { parse(text)?.symbols ?? text }

    static func name(forKeyCode keyCode: UInt32) -> String? { names[keyCode] }

    static let names: [UInt32: String] = {
        var names: [UInt32: String] = [
            0x00: "A", 0x0B: "B", 0x08: "C", 0x02: "D", 0x0E: "E", 0x03: "F", 0x05: "G", 0x04: "H", 0x22: "I",
            0x26: "J", 0x28: "K", 0x25: "L", 0x2E: "M", 0x2D: "N", 0x1F: "O", 0x23: "P", 0x0C: "Q", 0x0F: "R",
            0x01: "S", 0x11: "T", 0x20: "U", 0x09: "V", 0x0D: "W", 0x07: "X", 0x10: "Y", 0x06: "Z",
            0x1D: "0", 0x12: "1", 0x13: "2", 0x14: "3", 0x15: "4", 0x17: "5", 0x16: "6", 0x1A: "7", 0x1C: "8", 0x19: "9",
            0x31: "Space", 0x7B: "Left", 0x7C: "Right", 0x7D: "Down", 0x7E: "Up",
            0x73: "Home", 0x77: "End", 0x74: "PageUp", 0x79: "PageDown",
        ]
        let functionKeys: [UInt32] = [0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D,
                                      0x67, 0x6F, 0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A]
        for (index, code) in functionKeys.enumerated() { names[code] = "F\(index + 1)" }
        return names
    }()

    private static let codes: [String: UInt32] = Dictionary(uniqueKeysWithValues: names.map { ($0.value.uppercased(), $0.key) })

    private static let symbolNames: [UInt32: String] = [
        0x31: "Space", 0x7B: "←", 0x7C: "→", 0x7D: "↓", 0x7E: "↑", 0x73: "↖", 0x77: "↘", 0x74: "⇞", 0x79: "⇟",
    ]
}
