import AppKit
import SwiftUI

// Development tool, not shipped:
//   preview render <folder>                                renders Settings, bubble, indicator and toast offscreen
//   preview translate <claude|codex> <model> <target> <my language> <text>   runs a real engine translation
//   preview install-update <copy of Inline Translator.app>  installs the latest GitHub macOS release into that copy
// Nothing is shown on screen and no keys are sent, so it never disturbs the desktop in use.

let arguments = CommandLine.arguments
guard arguments.count >= 3 else {
    FileHandle.standardError.write(Data("usage: preview render <folder> | translate <provider> <model> <target> <home> <text> | install-update <app>\n".utf8))
    exit(2)
}

struct Failure: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}

@MainActor
enum Renderer {
    static func snapshot(_ view: NSView, size: CGSize, to url: URL) throws {
        let window = NSWindow(contentRect: CGRect(origin: .zero, size: size), styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = view
        view.frame = CGRect(origin: .zero, size: size)
        for _ in 0..<4 {
            view.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.15))
        }
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw Failure("No bitmap for \(url.lastPathComponent)") }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try bitmap.representation(using: .png, properties: [:])!.write(to: url)
        print("wrote \(url.path)")
    }

    static func render(_ folder: URL) throws {
        _ = NSApplication.shared
        NSApp.setActivationPolicy(.prohibited)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let store = FileManager.default.temporaryDirectory.appendingPathComponent("InlineTranslator-preview-\(UUID().uuidString).json")
        for language in AppSettings.uiLanguages {
            let settings = AppSettings().normalized().with(\.uiLanguage, language)
            let app = AppController(settingsURL: store, settings: settings, firstRun: false)
            for (tab, trusted) in [("translation", false), ("translation", true), ("engines", true), ("general", true)] {
                let model = SettingsModel(app: app)
                model.tab = tab
                model.accessibilityTrusted = trusted
                model.signIn = [.claude: .signedIn, .codex: .signedOut]
                if tab == "engines" {
                    model.tests[.primary] = EngineTest(state: .succeeded, message: app.text.t("Test succeeded · {0} · {1}s\n{2}",
                                                                                              "Claude · Haiku 5.5", "2.4", "Hi! What time is the meeting tomorrow?"))
                }
                let name = "settings-\(language)-\(tab)\(trusted ? "" : "-permission").png"
                try snapshot(NSHostingView(rootView: SettingsView(model: model)), size: CGSize(width: 640, height: 1180),
                             to: folder.appendingPathComponent(name))
            }
            let text = app.text
            for dark in [false, true] {
                let bubble = TranslationBubble.content(title: "→ English · Claude · Haiku 5.5",
                                                       body: "Please review the attached slides by tomorrow. Let me know if anything looks off.",
                                                       hint: text.t("Translation · press any key or click to close"), dark: dark)
                try snapshot(bubble, size: bubble.fittingSize, to: folder.appendingPathComponent("bubble-\(language)-\(dark ? "dark" : "light").png"))
            }
            let busy = BusyIndicator.content(text: text.t("Translating to {0}… · Esc to cancel", "English"))
            try snapshot(busy, size: busy.fittingSize, to: folder.appendingPathComponent("busy-\(language).png"))
            let toast = Toast.content(text.t("{0} needs you to sign in. Click here to sign in.", "Codex"), kind: .warning)
            try snapshot(toast, size: toast.fittingSize, to: folder.appendingPathComponent("toast-\(language).png"))
        }
        try? FileManager.default.removeItem(at: store)
    }
}

func translate(_ arguments: [String]) async throws {
    guard arguments.count >= 7, let provider = Provider(rawValue: arguments[2]), let target = Languages.find(arguments[4]) else {
        throw Failure("usage: preview translate <claude|codex> <model> <target> <my language> <text>")
    }
    let environment = EngineEnvironment(shellPath: await ShellPath.load())
    var settings = AppSettings().normalized()
    settings.primary = EngineChoice(provider: provider, model: arguments[3])
    settings.fallback = .none
    settings.homeLanguage = arguments[5]
    let started = Date()
    let result = try await Translator { engine, instructions, text in
        try await EngineCli.translate(engine, instructions: instructions, text: text, environment: environment)
    }.translate(settings, target: target, text: arguments[6])
    print("engine: \(result.engine.displayName)")
    print("output: \((result.output ?? target).code)")
    print(String(format: "seconds: %.1f", Date().timeIntervalSince(started)))
    print("text: \(result.text)")
}

func installUpdate(_ arguments: [String]) async throws {
    let bundle = URL(fileURLWithPath: arguments[2]).standardizedFileURL
    func version() throws -> String {
        let data = try Data(contentsOf: bundle.appendingPathComponent("Contents/Info.plist"))
        let plist = try PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any]
        return plist?["CFBundleShortVersionString"] as? String ?? "?"
    }
    guard let release = try await UpdateChecker.fetchLatest() else { throw Failure("No macOS release found.") }
    print("installed: \(try version()), latest: \(release.tag)")
    try await UpdateInstaller.install(release, into: bundle, bundleID: "io.github.hsoo-kim.inline-translator")
    print("after update: \(try version())")
}

do {
    switch arguments[1] {
    case "render": try await MainActor.run { try Renderer.render(URL(fileURLWithPath: arguments[2])) }
    case "translate": try await translate(arguments)
    case "install-update": try await installUpdate(arguments)
    default: throw Failure("unknown command \(arguments[1])")
    }
} catch {
    FileHandle.standardError.write(Data("preview failed: \(error.localizedDescription) \(error)\n".utf8))
    exit(1)
}
