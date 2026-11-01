import Foundation

struct AppEntry: Hashable, Identifiable {
    let bundleID: String
    let name: String
    let path: String?
    let running: Bool

    var id: String { bundleID.lowercased() }
}

enum AppCatalog {
    private static let chatApps: Set<String> = [
        "ru.keepcoder.telegram", "org.telegram.desktop", "com.kakao.kakaotalkmac", "com.tinyspeck.slackmacgap",
        "com.hnc.discord", "net.whatsapp.whatsapp", "desktop.whatsapp", "jp.naver.line.mac", "com.microsoft.teams2",
        "com.microsoft.teams", "org.whispersystems.signal-desktop", "com.apple.mobilesms", "com.tencent.xinwechat",
        "com.tencent.qq", "com.facebook.archon", "com.viber.osx", "im.riot.app", "com.mattermost.desktop",
        "us.zoom.xos", "com.nhncorp.nateonmac", "com.tosslab.jandi",
    ]

    /// Names for the default apps, shown even when an app is not installed on this Mac.
    static let knownNames: [String: String] = [
        "ru.keepcoder.telegram": "Telegram", "org.telegram.desktop": "Telegram Desktop", "com.kakao.kakaotalkmac": "KakaoTalk",
        "com.tinyspeck.slackmacgap": "Slack", "com.hnc.discord": "Discord", "net.whatsapp.whatsapp": "WhatsApp",
        "jp.naver.line.mac": "LINE", "com.microsoft.teams2": "Microsoft Teams", "org.whispersystems.signal-desktop": "Signal",
        "com.apple.mobilesms": "Messages",
    ]

    static func isKnownChatApp(_ bundleID: String) -> Bool { chatApps.contains(bundleID.lowercased()) }

    static func merge(installed: [AppEntry], running: [AppEntry], excluding: Set<String> = []) -> [AppEntry] {
        var apps: [String: AppEntry] = [:]
        for app in installed where !AppSettings.normalizeAppID(app.bundleID).isEmpty && !excluding.contains(app.id) {
            if let known = apps[app.id], known.name.count <= app.name.count { continue }
            apps[app.id] = app
        }
        for app in running where !AppSettings.normalizeAppID(app.bundleID).isEmpty && !excluding.contains(app.id) {
            let known = apps[app.id]
            apps[app.id] = AppEntry(bundleID: known?.bundleID ?? app.bundleID, name: known?.name ?? app.name,
                                    path: known?.path ?? app.path, running: true)
        }
        return apps.values.sorted {
            $0.running != $1.running ? $0.running : $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending
        }
    }
}
