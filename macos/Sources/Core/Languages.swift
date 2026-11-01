import Foundation

struct TargetLanguage: Hashable, Sendable {
    let code: String
    let englishName: String
    let nativeName: String

    var label: String { nativeName == englishName ? englishName : "\(nativeName) (\(englishName))" }
}

enum Languages {
    static let all: [TargetLanguage] = [
        TargetLanguage(code: "en", englishName: "English", nativeName: "English"),
        TargetLanguage(code: "ko", englishName: "Korean", nativeName: "한국어"),
        TargetLanguage(code: "ja", englishName: "Japanese", nativeName: "日本語"),
        TargetLanguage(code: "zh-CN", englishName: "Simplified Chinese", nativeName: "简体中文"),
        TargetLanguage(code: "zh-TW", englishName: "Traditional Chinese", nativeName: "繁體中文"),
        TargetLanguage(code: "es", englishName: "Spanish", nativeName: "Español"),
        TargetLanguage(code: "fr", englishName: "French", nativeName: "Français"),
        TargetLanguage(code: "de", englishName: "German", nativeName: "Deutsch"),
        TargetLanguage(code: "pt", englishName: "Portuguese", nativeName: "Português"),
        TargetLanguage(code: "it", englishName: "Italian", nativeName: "Italiano"),
        TargetLanguage(code: "ru", englishName: "Russian", nativeName: "Русский"),
        TargetLanguage(code: "vi", englishName: "Vietnamese", nativeName: "Tiếng Việt"),
        TargetLanguage(code: "th", englishName: "Thai", nativeName: "ไทย"),
        TargetLanguage(code: "id", englishName: "Indonesian", nativeName: "Bahasa Indonesia"),
        TargetLanguage(code: "ar", englishName: "Arabic", nativeName: "العربية"),
        TargetLanguage(code: "hi", englishName: "Hindi", nativeName: "हिन्दी"),
        TargetLanguage(code: "tr", englishName: "Turkish", nativeName: "Türkçe"),
        TargetLanguage(code: "nl", englishName: "Dutch", nativeName: "Nederlands"),
        TargetLanguage(code: "pl", englishName: "Polish", nativeName: "Polski"),
    ]

    static func find(_ code: String?) -> TargetLanguage? {
        guard let code else { return nil }
        return all.first { $0.code.caseInsensitiveCompare(code) == .orderedSame }
    }

    /// Maps a BCP 47 identifier such as "ko-KR", "zh-Hant-TW" or "pt-BR" to a supported language, English otherwise.
    static func fromIdentifier(_ identifier: String) -> TargetLanguage {
        let parts = identifier.replacingOccurrences(of: "_", with: "-").split(separator: "-").map(String.init)
        guard let language = parts.first?.lowercased() else { return all[0] }
        if language == "zh" {
            let traditional = parts.dropFirst().contains { ["Hant", "TW", "HK", "MO"].contains($0) }
            return find(traditional ? "zh-TW" : "zh-CN")!
        }
        return find(language) ?? all[0]
    }

    static func system() -> TargetLanguage { fromIdentifier(Locale.preferredLanguages.first ?? "en") }

    static func home(_ code: String) -> TargetLanguage { find(code) ?? system() }
}
