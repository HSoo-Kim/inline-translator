import AppKit
import Carbon.HIToolbox
import PDFKit
import SwiftUI
import WebKit

// Development tool, not shipped:
//   preview render <folder>                                renders Settings, bubble, indicator and toast offscreen
//   preview translate <claude|codex> <model> <target> <my language> <text>   runs a real engine translation
//   preview install-update <copy of Inline Translator.app>  installs the latest GitHub macOS release into that copy
//   preview context                                        reads surrounding context from an offscreen text view, web page and PDF
//   preview keys                                           shows which keys Cmd+A/C/V use in the current and other layouts
//   preview context-of <pid> [<copied text>]               reads the context of the current selection in a running app
// Nothing is shown on screen and no keys are sent, so it never disturbs the desktop in use.

let arguments = CommandLine.arguments
guard arguments.count >= 2 else {
    FileHandle.standardError.write(Data("usage: preview render <folder> | translate <provider> <model> <target> <home> <text> | install-update <app> | context\n".utf8))
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

@Sendable func translate(_ arguments: [String]) async throws {
    guard arguments.count >= 7, let provider = Provider(rawValue: arguments[2]), let target = Languages.find(arguments[4]) else {
        throw Failure("usage: preview translate <claude|codex> <model> <target> <my language> <text> [<text before> <text after>]")
    }
    let environment = EngineEnvironment(shellPath: await ShellPath.load())
    var settings = AppSettings().normalized()
    settings.primary = EngineChoice(provider: provider, model: arguments[3])
    settings.fallback = .none
    settings.homeLanguage = arguments[5]
    let context = arguments.count >= 9 ? ContextWindow.clip(before: arguments[7], after: arguments[8]) : nil
    let started = Date()
    let result = try await Translator { engine, instructions, text in
        try await EngineCli.translate(engine, instructions: instructions, text: text, environment: environment)
    }.translate(settings, target: target, text: arguments[6], context: context)
    print("engine: \(result.engine.displayName)")
    print("output: \((result.output ?? target).code)")
    print(String(format: "seconds: %.1f", Date().timeIntervalSince(started)))
    print("text: \(result.text)")
}

@Sendable func installUpdate(_ arguments: [String]) async throws {
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

/// The Cmd+A/C/V key codes per layout, compared with what each layout must use.
func checkKeys() throws {
    let us = [kVK_ANSI_A, kVK_ANSI_C, kVK_ANSI_V]
    let expected: [String: [Int]] = [
        "com.apple.keylayout.US": us, "com.apple.keylayout.ABC": us,
        "com.apple.keylayout.French": [kVK_ANSI_Q, kVK_ANSI_C, kVK_ANSI_V],
        "com.apple.keylayout.Dvorak": [kVK_ANSI_A, kVK_ANSI_I, kVK_ANSI_Period],
        "com.apple.keylayout.DVORAK-QWERTYCMD": us,
        "com.apple.keylayout.German": us,
    ]
    var failures: [String] = []
    for (id, codes) in expected.sorted(by: { $0.key < $1.key }) {
        let filter = [kTISPropertyInputSourceID as String: id] as CFDictionary
        guard let sources = TISCreateInputSourceList(filter, true)?.takeRetainedValue() as? [TISInputSource], let source = sources.first else {
            print("\(id): layout not installed")
            continue
        }
        let actual = [KeyCode.command("a", fallback: kVK_ANSI_A, layout: source), KeyCode.command("c", fallback: kVK_ANSI_C, layout: source),
                      KeyCode.command("v", fallback: kVK_ANSI_V, layout: source)]
        print("\(id): \(actual)\(actual == codes ? "" : " expected \(codes)")")
        if actual != codes { failures.append(id) }
    }
    print("current: \([KeyCode.a, KeyCode.c, KeyCode.v])")
    if !failures.isEmpty { throw Failure("Wrong keys for \(failures.joined(separator: ", "))") }
}

/// Reads surrounding context the way the app does: from another process, through Accessibility. A child process shows
/// a text view, a web page and a PDF in a window placed far off screen, with the same sentence selected in each.
@MainActor
enum ContextCheck {
    static let paragraphs = ["The release checklist is attached below.", "Last week we agreed to ship on Friday.",
                             "Let's push it to next week.", "Tell me if anything else blocks it."]
    static var selected: String { paragraphs[2] }
    static let roles = ["AXTextArea", "AXWebArea", "PDF"]

    static func pdf(singleLine: Bool) -> PDFDocument {
        let data = NSMutableData()
        var box = CGRect(x: 0, y: 0, width: 595, height: 842)
        let context = CGContext(consumer: CGDataConsumer(data: data)!, mediaBox: &box, nil)!
        context.beginPDFPage(nil)
        let lines = singleLine ? ["Before. \(selected) After."] : paragraphs
        for (index, line) in lines.enumerated() {
            let text = NSAttributedString(string: line, attributes: [.font: NSFont.systemFont(ofSize: 18)])
            context.textPosition = CGPoint(x: 60, y: 760 - CGFloat(index) * 34)
            CTLineDraw(CTLineCreateWithAttributedString(text), context)
        }
        context.endPDFPage()
        context.closePDF()
        return PDFDocument(data: data as Data)!
    }

    static func host() -> Never {
        _ = NSApplication.shared
        // Accessory: no Dock icon and never activated, but AppKit still serves its Accessibility tree.
        NSApp.setActivationPolicy(.accessory)
        NSApp.finishLaunching()
        let window = NSWindow(contentRect: CGRect(x: -30000, y: -30000, width: 600, height: 900), styleMask: [.titled],
                              backing: .buffered, defer: false)
        let stack = NSStackView()
        stack.orientation = .vertical
        let textView = NSTextView(frame: CGRect(x: 0, y: 0, width: 600, height: 150))
        textView.string = paragraphs.joined(separator: "\n")
        let web = WKWebView(frame: CGRect(x: 0, y: 0, width: 600, height: 250))
        let pdfView = PDFView(frame: CGRect(x: 0, y: 0, width: 600, height: 450))
        pdfView.document = pdf(singleLine: CommandLine.arguments.contains("single-line"))
        let field = NSTextField(frame: CGRect(x: 0, y: 0, width: 600, height: 30))
        field.stringValue = selected
        field.setAccessibilityIdentifier("unrelated-field")
        for view in [textView, web, pdfView, field] as [NSView] {
            stack.addArrangedSubview(view)
            view.heightAnchor.constraint(equalToConstant: view.frame.height).isActive = true
        }
        window.contentView = stack
        window.orderFrontRegardless()
        textView.setSelectedRange((textView.string as NSString).range(of: selected))
        pdfView.setCurrentSelection(pdfView.document?.findString(selected, withOptions: []).first, animate: false)
        let html = paragraphs.enumerated().map { $0.offset == 2 ? "<p><span id=t>\($0.element)</span></p>" : "<p>\($0.element)</p>" }
        web.loadHTMLString("<html><body>" + html.joined() + "</body></html>", baseURL: nil)
        let deadline = Date().addingTimeInterval(15)
        while web.isLoading && Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.05)) }
        var selectedOnPage = false
        web.evaluateJavaScript("var r=document.createRange();r.selectNodeContents(document.getElementById('t').firstChild);" +
                               "var s=getSelection();s.removeAllRanges();s.addRange(r);s.toString()") { _, _ in selectedOnPage = true }
        while !selectedOnPage && Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.05)) }
        print("ready")
        fflush(stdout)
        while true { RunLoop.main.run(until: Date().addingTimeInterval(1)) }
    }

    static func run(singleLine: Bool) throws {
        let host = Process()
        host.executableURL = URL(fileURLWithPath: Bundle.main.executablePath ?? CommandLine.arguments[0])
        host.arguments = ["context-host"] + (singleLine ? ["single-line"] : [])
        let output = Pipe()
        host.standardOutput = output
        try host.run()
        defer { host.terminate() }
        var buffer = Data()
        while !String(decoding: buffer, as: UTF8.self).contains("ready") {
            let chunk = output.fileHandleForReading.availableData
            if chunk.isEmpty { throw Failure("The context host exited early.") }
            buffer.append(chunk)
        }
        var found: [String: AXUIElement] = [:]
        func walk(_ element: AXUIElement, _ depth: Int) {
            guard depth < 14 else { return }
            let role = Accessibility.attribute(element, kAXRoleAttribute) as? String ?? "?"
            if ["AXTextArea", "AXWebArea"].contains(role), found[role] == nil { found[role] = element }
            if found["PDF"] == nil, Accessibility.isPDFDocument(element, role: role), role != "AXPage" { found["PDF"] = element }
            if role == kAXTextFieldRole, Accessibility.attribute(element, kAXIdentifierAttribute) as? String == "unrelated-field" {
                found["Field"] = element
            }
            let children = Accessibility.attribute(element, kAXChildrenAttribute) as? [AXUIElement]
                ?? Accessibility.attribute(element, kAXWindowsAttribute) as? [AXUIElement] ?? []
            for child in children { walk(child, depth + 1) }
        }
        // The tree fills in shortly after the host reports ready.
        for _ in 0..<20 where found.count < roles.count + 1 {
            walk(Accessibility.application(host.processIdentifier), 0)
            if found.count < roles.count + 1 { Thread.sleep(forTimeInterval: 0.5) }
        }
        var missing: [String] = []
        for role in roles {
            let element = found[role]
            if let element, let context = Accessibility.selectionContext(element, selected: selected) {
                let singlePDF = role == "PDF" && singleLine
                guard context.before.contains(singlePDF ? "Before." : paragraphs[1]),
                      context.after.contains(singlePDF ? "After." : paragraphs[3]),
                      role != "PDF" || Accessibility.isReadOnly(element) else {
                    throw Failure("Incorrect context or read-only status from \(role): \(context)")
                }
                print("\(role): read-only=\(Accessibility.isReadOnly(element)) before=\(context.before.debugDescription) after=\(context.after.debugDescription)")
            } else {
                print("\(role): \(element == nil ? "element not found" : "no context")")
                missing.append(role)
            }
        }
        guard let field = found["Field"], Accessibility.selectionContext(field, selected: selected) == nil else {
            throw Failure("A selection in an unrelated field picked up PDF context.")
        }
        print("Unrelated field: no PDF context")
        if !missing.isEmpty { throw Failure("No context from \(missing.joined(separator: ", "))") }
    }
}

func fail(_ error: Error) -> Never {
    FileHandle.standardError.write(Data("preview failed: \(error.localizedDescription) \(error)\n".utf8))
    exit(1)
}

// Synchronous top level, so nested run loops still drain the main queue (WebKit and AppKit callbacks rely on it).
func runAsync(_ body: @escaping @Sendable () async throws -> Void) -> Never {
    Task {
        do { try await body() } catch { fail(error) }
        exit(0)
    }
    while true { RunLoop.main.run(until: Date().addingTimeInterval(1)) }
}

do {
    switch arguments[1] {
    case "render": try MainActor.assumeIsolated { try Renderer.render(URL(fileURLWithPath: arguments[2])) }
    case "context": try MainActor.assumeIsolated { try ContextCheck.run(singleLine: arguments.contains("single-line")) }
    case "keys": try checkKeys()
    case "context-of":
        guard arguments.count >= 3, let pid = pid_t(arguments[2]) else { throw Failure("usage: preview context-of <pid> [<copied text>]") }
        let focused = Accessibility.focusedElement(pid)
        // The app matches what Cmd+C copied; without it, use the text the focused element reports as selected.
        let selected = arguments.count > 3 ? arguments[3] : focused.flatMap { Accessibility.attribute($0, kAXSelectedTextAttribute) as? String } ?? ""
        let started = Date()
        let context = Accessibility.selectionContext(focused, selected: selected)
        let role = focused.flatMap { Accessibility.attribute($0, kAXRoleAttribute) as? String } ?? "none"
        print("focused: \(role) read-only: \(Accessibility.isReadOnly(focused)) selected: \(selected.prefix(80).debugDescription)")
        print(String(format: "context (%.2fs): ", Date().timeIntervalSince(started))
              + (context.map { "before=\($0.before.suffix(200).debugDescription) after=\($0.after.prefix(200).debugDescription)" } ?? "none"))
    case "context-host": MainActor.assumeIsolated { ContextCheck.host() }
    case "translate": runAsync { try await translate(arguments) }
    case "install-update": runAsync { try await installUpdate(arguments) }
    default: throw Failure("unknown command \(arguments[1])")
    }
} catch {
    fail(error)
}
