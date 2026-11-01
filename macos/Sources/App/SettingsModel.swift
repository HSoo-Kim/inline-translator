import AppKit
import SwiftUI

enum EngineSlot: String { case primary, fallback }

struct EngineTest: Equatable {
    enum State: Equatable { case running, succeeded, failed }
    let state: State
    let message: String
}

/// Draft of the Settings window. Save starts disabled and turns on only for a changed, valid draft.
@MainActor
final class SettingsModel: ObservableObject {
    let text: UiText
    private weak var app: AppController?
    let original: AppSettings
    let originalLoginItem: Bool
    @Published var draft: AppSettings { didSet { if draft.presets != oldValue.presets || draft.readingHotkey != oldValue.readingHotkey || draft.readingHotkeyEnabled != oldValue.readingHotkeyEnabled { pruneRegistrationErrors(oldValue) } } }
    @Published var openAtLogin: Bool
    @Published var tab = "translation"
    @Published var registrationErrors: [Int: String] = [:]
    @Published var saveError: String?
    @Published var signIn: [Provider: SignIn] = [:]
    @Published var tests: [EngineSlot: EngineTest] = [:]
    @Published var update: ReleaseInfo?
    @Published var updateStatus: (message: String, error: Bool)?
    @Published var checkingUpdates = false
    @Published var accessibilityTrusted = Accessibility.isTrusted
    private var signInTask: Task<Void, Never>?
    var close: () -> Void = {}

    init(app: AppController) {
        self.app = app
        text = app.text
        original = app.settings
        draft = app.settings
        originalLoginItem = LoginItem.isEnabled
        openAtLogin = originalLoginItem
        update = app.update
    }

    var hotkeyProblems: [Int: HotkeyProblem] { SettingsValidation.hotkeyErrors(draft) }
    var hasChanges: Bool { draft != original || openAtLogin != originalLoginItem }
    var canSave: Bool { hasChanges && hotkeyProblems.isEmpty && registrationErrors.isEmpty }

    func hotkeyError(_ index: Int) -> String? {
        switch hotkeyProblems[index] {
        case .missing: text.t("Choose a hotkey.")
        case .invalid: text.t("Use ⌃, ⌥ or ⌘ with a letter, number or function key.")
        case .duplicate: text.t("This hotkey is also used by another translation shortcut.")
        case nil: registrationErrors[index]
        }
    }

    private func pruneRegistrationErrors(_ old: AppSettings) {
        for index in registrationErrors.keys {
            let changed = index < AppSettings.presetCount
                ? draft.presets[index] != old.presets[index]
                : draft.readingHotkey != old.readingHotkey || draft.readingHotkeyEnabled != old.readingHotkeyEnabled
            if changed { registrationErrors[index] = nil }
        }
    }

    func suspendHotkeys(_ suspended: Bool) { app?.suspendHotkeys(suspended) }

    func save() {
        guard canSave, let app else { return }
        saveError = nil
        do {
            if LoginItem.isAvailable && openAtLogin != LoginItem.isEnabled { try LoginItem.set(openAtLogin) }
            let failed = try app.saveSettings(draft)
            guard failed.isEmpty else {
                let hotkeys = draft.presets.map { ($0.enabled, $0.hotkey) } + [(draft.readingHotkeyEnabled, draft.readingHotkey)]
                for (index, entry) in hotkeys.enumerated() where entry.0 && failed.contains(where: { $0.caseInsensitiveCompare(entry.1) == .orderedSame }) {
                    registrationErrors[index] = text.t("This hotkey is already used by another app. Choose another.")
                }
                tab = "translation"
                return
            }
            close()
        } catch {
            ErrorLog.write("Could not save settings", error)
            saveError = text.t("Could not save settings: {0}", error.localizedDescription)
        }
    }

    // MARK: Engines

    func engine(_ slot: EngineSlot) -> EngineChoice { slot == .primary ? draft.primary : draft.fallback }

    func setEngine(_ slot: EngineSlot, _ engine: EngineChoice) {
        if slot == .primary { draft.primary = engine } else { draft.fallback = engine }
    }

    func test(_ slot: EngineSlot) {
        let engine = engine(slot)
        guard engine.provider != .none, tests[slot]?.state != .running, let app else { return }
        let target = Languages.find(draft.presets[0].targetLanguage) ?? Languages.all[0]
        let sample = target.code == "ko" ? "Hi! What time is the meeting tomorrow? I might be a bit late."
            : "안녕하세요! 내일 회의 몇 시에 하나요? 조금 늦을 것 같아요."
        var settings = draft
        settings.primary = engine
        settings.fallback = .none
        tests[slot] = EngineTest(state: .running, message: text.t("Testing {0}…", engine.displayName))
        let translator = app.translator()
        let started = Date()
        Task {
            do {
                let result = try await translator.translate(settings, target: target, text: sample)
                let seconds = String(format: "%.1f", Date().timeIntervalSince(started))
                tests[slot] = EngineTest(state: .succeeded,
                                         message: text.t("Test succeeded · {0} · {1}s\n{2}", engine.displayName, seconds, result.text))
            } catch let failure as TranslationFailed {
                tests[slot] = EngineTest(state: .failed, message: text.t("Test failed · {0}", failure.localizedDescription))
                if EngineCli.isSignInError(failure.primary) { refreshSignIn() }
            } catch {
                tests[slot] = EngineTest(state: .failed, message: text.t("Test failed · {0}", error.localizedDescription))
            }
        }
    }

    func refreshSignIn() {
        guard signInTask == nil, let app else { return }
        signIn = [:]
        let environment = app.environment
        signInTask = Task {
            async let claude = EngineCli.checkSignIn(.claude, environment: environment)
            async let codex = EngineCli.checkSignIn(.codex, environment: environment)
            let (claudeState, codexState) = await (claude, codex)
            signIn = [.claude: claudeState, .codex: codexState]
            signInTask = nil
        }
    }

    func executablePath(_ provider: Provider) -> String? { app?.environment.resolve(provider) }

    func accountAction(_ provider: Provider) {
        if signIn[provider] == .notInstalled {
            NSWorkspace.shared.open(EngineCli.installPage(provider))
            return
        }
        do { try app?.startSignIn(provider) } catch {
            ErrorLog.write("Could not start sign-in", error)
            let alert = NSAlert()
            alert.messageText = error.localizedDescription
            alert.runModal()
        }
    }

    // MARK: Updates

    func setUpdateStatus(_ message: String, checking: Bool = false, error: Bool = false) {
        checkingUpdates = checking
        updateStatus = (message, error)
    }

    func checkUpdates() {
        setUpdateStatus(text.t("Checking for updates…"), checking: true)
        app?.requestUpdateCheck(manual: true)
    }

    func installUpdate() { Task { await app?.installUpdate() } }

    func refreshAccessibility() { accessibilityTrusted = Accessibility.isTrusted }

    func allowAccessibility() {
        Accessibility.requestTrust()
        Accessibility.openSettings()
    }
}
