import Foundation

enum Provider: String, Codable, CaseIterable, Sendable {
    case none, claude, codex
}

struct EngineChoice: Codable, Hashable, Sendable {
    var provider: Provider
    var model: String

    static let none = EngineChoice(provider: .none, model: "")

    var displayName: String {
        guard provider != .none else { return "" }
        let label = EngineCli.suggestedModels(provider).first { $0.id == model }?.label ?? model
        return (provider == .claude ? "Claude · " : "Codex · ") + label
    }
}

struct HotkeyPreset: Codable, Hashable, Sendable {
    var enabled: Bool
    var hotkey: String
    var targetLanguage: String
}

enum HotkeyProblem: Equatable { case missing, invalid, duplicate }

enum SettingsValidation {
    /// Reading uses the index immediately after the writing presets.
    static func hotkeyErrors(_ settings: AppSettings) -> [Int: HotkeyProblem] {
        var errors: [Int: HotkeyProblem] = [:]
        var used: [Hotkey: Int] = [:]
        let entries = settings.presets.enumerated().map { ($0.offset, $0.element.enabled, $0.element.hotkey) }
            + [(AppSettings.presetCount, settings.readingHotkeyEnabled, settings.readingHotkey)]
        for (index, enabled, value) in entries where enabled {
            if value.trimmingCharacters(in: .whitespaces).isEmpty {
                errors[index] = .missing
            } else if let hotkey = Hotkey.parse(value) {
                if let previous = used[hotkey] {
                    errors[index] = .duplicate
                    errors[previous] = .duplicate
                } else {
                    used[hotkey] = index
                }
            } else {
                errors[index] = .invalid
            }
        }
        return errors
    }
}

struct AppSettings: Codable, Equatable, Sendable {
    static let presetCount = 3
    static let uiLanguages = ["ko", "en", "zh-CN", "ja"]
    static let tones = ["natural", "casual", "formal"]
    static let bubbleThemes = ["light", "dark"]
    static let bubblePositions = ["mouse", "selection"]
    static let defaultClaudeModel = "claude-haiku-5-5"
    static let defaultCodexModel = "gpt-5.6-luna"

    static let defaultPresets = [
        HotkeyPreset(enabled: true, hotkey: "Ctrl+Option+T", targetLanguage: "en"),
        HotkeyPreset(enabled: false, hotkey: "Ctrl+Option+J", targetLanguage: "ja"),
        HotkeyPreset(enabled: false, hotkey: "Ctrl+Option+K", targetLanguage: "ko"),
    ]

    /// Bundle identifiers of chat apps where no selection means "the whole message box".
    static let defaultWholeInputApps = [
        "ru.keepcoder.Telegram", "org.telegram.desktop", "com.kakao.KakaoTalkMac", "com.tinyspeck.slackmacgap",
        "com.hnc.Discord", "net.whatsapp.WhatsApp", "jp.naver.line.mac", "com.microsoft.teams2",
        "org.whispersystems.signal-desktop", "com.apple.MobileSMS",
    ]

    var uiLanguage = ""
    var primary = EngineChoice(provider: .claude, model: AppSettings.defaultClaudeModel)
    var fallback = EngineChoice(provider: .codex, model: AppSettings.defaultCodexModel)
    var presets = AppSettings.defaultPresets
    var readingHotkeyEnabled = true
    var readingHotkey = "Ctrl+Option+R"
    var readingTargetLanguage = ""
    var tone = "natural"
    var homeLanguage = ""
    var wholeInputApps = AppSettings.defaultWholeInputApps
    var bubbleTheme = "light"
    var bubblePosition = "mouse"
    var checkForUpdates = true
    var autoInstallUpdates = true

    init() {}

    // Missing keys keep their defaults so older or hand-edited files still load.
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        func read<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T {
            (try? values.decodeIfPresent(T.self, forKey: key)) ?? fallback
        }
        let defaults = AppSettings()
        uiLanguage = read(.uiLanguage, defaults.uiLanguage)
        primary = read(.primary, defaults.primary)
        fallback = read(.fallback, defaults.fallback)
        presets = read(.presets, defaults.presets)
        readingHotkeyEnabled = read(.readingHotkeyEnabled, defaults.readingHotkeyEnabled)
        readingHotkey = read(.readingHotkey, defaults.readingHotkey)
        readingTargetLanguage = read(.readingTargetLanguage, defaults.readingTargetLanguage)
        tone = read(.tone, defaults.tone)
        homeLanguage = read(.homeLanguage, defaults.homeLanguage)
        wholeInputApps = read(.wholeInputApps, defaults.wholeInputApps)
        bubbleTheme = read(.bubbleTheme, defaults.bubbleTheme)
        bubblePosition = read(.bubblePosition, defaults.bubblePosition)
        checkForUpdates = read(.checkForUpdates, defaults.checkForUpdates)
        autoInstallUpdates = read(.autoInstallUpdates, defaults.autoInstallUpdates)
    }

    static func defaultModel(_ provider: Provider) -> String {
        switch provider {
        case .claude: defaultClaudeModel
        case .codex: defaultCodexModel
        case .none: ""
        }
    }

    static func isValidModel(_ model: String?) -> Bool {
        guard let model = model?.trimmingCharacters(in: .whitespaces), !model.isEmpty, model.count <= 100 else { return false }
        return model.unicodeScalars.allSatisfy { scalar in
            (scalar.isASCII && CharacterSet.alphanumerics.contains(scalar)) || ".-_:/[]".unicodeScalars.contains(scalar)
        }
    }

    static func normalizeAppID(_ id: String?) -> String {
        let trimmed = (id ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.count <= 255 else { return "" }
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_")
        return trimmed.unicodeScalars.allSatisfy { allowed.contains($0) } ? trimmed : ""
    }

    func normalized() -> AppSettings {
        var result = self
        var normalizedPresets: [HotkeyPreset] = []
        var used = Set<Hotkey>()
        for index in 0..<AppSettings.presetCount {
            var preset = index < presets.count ? presets[index] : AppSettings.defaultPresets[index]
            if index >= presets.count { preset.enabled = false }
            let language = Languages.find(preset.targetLanguage)?.code ?? "en"
            let hotkey = Hotkey.parse(preset.hotkey)
            let enabled = preset.enabled && hotkey != nil && used.insert(hotkey!).inserted
            normalizedPresets.append(HotkeyPreset(enabled: enabled, hotkey: hotkey?.description ?? "", targetLanguage: language))
        }
        let reading = Hotkey.parse(readingHotkey)

        var seen = Set<String>()
        let apps = wholeInputApps.map(AppSettings.normalizeAppID)
            .filter { !$0.isEmpty && seen.insert($0.lowercased()).inserted }
            .prefix(50)

        let primary = AppSettings.normalizeEngine(primary, allowNone: false)
            ?? EngineChoice(provider: .claude, model: AppSettings.defaultClaudeModel)
        var fallback = AppSettings.normalizeEngine(fallback, allowNone: true) ?? EngineChoice.none
        if fallback == primary { fallback = .none }

        result.uiLanguage = AppSettings.uiLanguages.first { $0.caseInsensitiveCompare(uiLanguage) == .orderedSame } ?? ""
        result.primary = primary
        result.fallback = fallback
        result.presets = normalizedPresets
        result.readingHotkeyEnabled = readingHotkeyEnabled && reading != nil && !used.contains(reading!)
        result.readingHotkey = reading?.description ?? ""
        result.readingTargetLanguage = Languages.find(readingTargetLanguage)?.code
            ?? normalizedPresets.first(where: \.enabled)?.targetLanguage ?? normalizedPresets[0].targetLanguage
        result.tone = AppSettings.tones.contains(tone) ? tone : "natural"
        result.homeLanguage = Languages.find(homeLanguage)?.code ?? ""
        result.bubbleTheme = AppSettings.bubbleThemes.contains(bubbleTheme) ? bubbleTheme : "light"
        result.bubblePosition = AppSettings.bubblePositions.contains(bubblePosition) ? bubblePosition : "mouse"
        result.wholeInputApps = Array(apps)
        return result
    }

    func with<Value>(_ path: WritableKeyPath<AppSettings, Value>, _ value: Value) -> AppSettings {
        var copy = self
        copy[keyPath: path] = value
        return copy
    }

    private static func normalizeEngine(_ engine: EngineChoice, allowNone: Bool) -> EngineChoice? {
        if engine.provider == .none { return allowNone ? EngineChoice.none : nil }
        let model = isValidModel(engine.model) ? engine.model.trimmingCharacters(in: .whitespaces) : defaultModel(engine.provider)
        return EngineChoice(provider: engine.provider, model: model)
    }
}

enum SettingsStore {
    static var defaultURL: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("InlineTranslator", isDirectory: true)
            .appendingPathComponent("settings.json")
    }

    /// nil when the file does not exist (first run); defaults when it cannot be parsed.
    static func load(_ url: URL) -> AppSettings? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        let settings = (try? JSONDecoder().decode(AppSettings.self, from: data)) ?? AppSettings()
        return settings.normalized()
    }

    static func save(_ url: URL, _ settings: AppSettings) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        try encoder.encode(settings.normalized()).write(to: url, options: .atomic)
    }
}
