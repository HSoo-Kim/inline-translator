import AppKit

@MainActor
final class AppController: NSObject, NSApplicationDelegate, NSMenuDelegate {
    static let readingHotkeyID = UInt32(AppSettings.presetCount + 1)
    private static let selectionProbe: TimeInterval = 0.35
    private static let copyWait: TimeInterval = 2
    private static let pasteSettle: UInt64 = 400_000_000

    private let settingsURL: URL
    private(set) var settings: AppSettings
    private let firstRun: Bool
    let text: UiText
    private(set) var environment = EngineEnvironment(shellPath: nil)
    private let hotkeys = HotkeyCenter()
    private let bubble = TranslationBubble()
    private let indicator = BusyIndicator()
    let toast = Toast()
    private var statusItem: NSStatusItem!
    private let menu = NSMenu()
    private let titleItem = NSMenuItem()
    private let accessibilityItem = NSMenuItem()
    private let updateItem = NSMenuItem()
    private let checkUpdatesItem = NSMenuItem()
    private let settingsItem = NSMenuItem()
    private let languageItem = NSMenuItem()
    private let pauseItem = NSMenuItem()
    private let quitItem = NSMenuItem()
    private var settingsWindow: SettingsWindowController?
    private(set) var update: ReleaseInfo?
    private var busy = false, installing = false, paused = false, hotkeysSuspended = false
    private(set) var checkingUpdates = false
    private var updateTimer: Timer?
    private var currentUpdateCheck: Task<Void, Never>?
    private var operation: Task<Void, Never>?
    private var signInReminded = Set<Provider>()
    /// Engines whose last real request failed for sign-in reasons. A CLI can still call an expired login "signed in"
    /// (`codex login status` does), so Settings shows these as needing sign-in until a request succeeds again.
    private(set) var signInFailures = Set<Provider>()

    init(settingsURL: URL, settings: AppSettings, firstRun: Bool) {
        self.settingsURL = settingsURL
        self.settings = settings
        self.firstRun = firstRun
        text = UiText(settings.uiLanguage)
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.mainMenu = MainMenu.build()
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        let image = NSImage(systemSymbolName: "character.bubble", accessibilityDescription: "Inline Translator")
        image?.isTemplate = true
        statusItem.button?.image = image
        statusItem.button?.setAccessibilityLabel("Inline Translator")
        toast.anchor = { [weak self] in self?.statusItem.button?.window?.frame }

        for item in [titleItem, accessibilityItem, NSMenuItem.separator(), updateItem, checkUpdatesItem, settingsItem,
                     languageItem, pauseItem, NSMenuItem.separator(), quitItem] { menu.addItem(item) }
        titleItem.isEnabled = false
        accessibilityItem.target = self
        accessibilityItem.action = #selector(allowAccessibility)
        updateItem.target = self
        updateItem.action = #selector(installUpdateFromMenu)
        updateItem.isHidden = true
        checkUpdatesItem.target = self
        checkUpdatesItem.action = #selector(checkUpdatesFromMenu)
        settingsItem.target = self
        settingsItem.action = #selector(showSettingsFromMenu)
        settingsItem.keyEquivalent = ","
        pauseItem.target = self
        pauseItem.action = #selector(togglePause)
        quitItem.target = self
        quitItem.action = #selector(quit)
        quitItem.keyEquivalent = "q"
        let languages = NSMenu()
        for (code, name) in SettingsLanguages.display {
            let item = NSMenuItem(title: name, action: #selector(chooseDisplayLanguage(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = code
            languages.addItem(item)
        }
        languageItem.submenu = languages
        menu.delegate = self
        menu.autoenablesItems = false
        statusItem.menu = menu

        warnIfNotInstalled()
        hotkeys.onPress = { [weak self] id in self?.hotkeyPressed(id) }
        applyHotkeys(notify: true)
        Task {
            let path = await ShellPath.load()
            environment = EngineEnvironment(shellPath: path)
            // Settings opened at first launch checked sign-in before the login shell's PATH was known.
            settingsWindow?.model.refreshSignIn()
        }
        updateTimer = Timer.scheduledTimer(withTimeInterval: UpdateChecker.checkInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.requestUpdateCheck() }
        }
        requestUpdateCheck()
        if firstRun {
            showSettings()
            toast.show(text.t("Inline Translator is running in the menu bar. Select text and press {0}.", firstHotkey), kind: .info)
        }
    }

    /// Opened straight from the disk image (or from a copy macOS moved to a temporary place), the app cannot update
    /// itself or open at login, so ask for the usual drag into Applications first.
    private func warnIfNotInstalled() {
        let path = Bundle.main.bundlePath
        guard path.hasPrefix("/Volumes/") || path.contains("/AppTranslocation/") else { return }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = text.t("Move Inline Translator to Applications")
        alert.informativeText = text.t("It is running from the disk image or a temporary copy, so it cannot update itself or open at login. Quit, drag Inline Translator into the Applications folder, and open it from there.")
        alert.addButton(withTitle: text.t("Quit"))
        alert.addButton(withTitle: text.t("Continue Anyway"))
        if alert.runModal() == .alertFirstButtonReturn { NSApp.terminate(nil) }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings()
        return false
    }

    func menuWillOpen(_ menu: NSMenu) {
        refreshTexts()
        requestUpdateCheck()
    }

    private var firstHotkey: String {
        settings.presets.first(where: \.enabled).map { Hotkey.display($0.hotkey) } ?? ""
    }

    func refreshTexts() {
        titleItem.title = "Inline Translator \(UpdateChecker.current)"
        accessibilityItem.title = text.t("Allow Accessibility…")
        accessibilityItem.isHidden = Accessibility.isTrusted
        settingsItem.title = text.t("Settings…")
        checkUpdatesItem.title = checkingUpdates ? text.t("Checking for updates…") : text.t("Check for updates")
        checkUpdatesItem.isEnabled = !checkingUpdates && !installing
        pauseItem.title = text.t("Pause hotkeys")
        pauseItem.state = paused ? .on : .off
        quitItem.title = text.t("Quit Inline Translator")
        languageItem.title = SettingsLanguages.label(text)
        for item in languageItem.submenu?.items ?? [] {
            let code = item.representedObject as? String ?? ""
            item.state = code == settings.uiLanguage ? .on : .off
            if code.isEmpty { item.title = text.t("System default") }
        }
        if let update { updateItem.title = text.t("Update to {0}", update.tag) }
        updateItem.isHidden = update == nil
        let active = settings.presets.filter(\.enabled)
            .map { "\(Hotkey.display($0.hotkey)) → \(Languages.find($0.targetLanguage)?.nativeName ?? "")" }
        statusItem.button?.toolTip = "Inline Translator" + (paused ? " · " + text.t("Paused") : "\n" + active.joined(separator: "\n"))
        statusItem.button?.appearsDisabled = paused
    }

    @discardableResult
    func applyHotkeys(notify: Bool) -> [String] {
        hotkeys.unregisterAll()
        var failed: [String] = []
        if !paused && !hotkeysSuspended {
            for (index, preset) in settings.presets.enumerated() where preset.enabled {
                if let hotkey = Hotkey.parse(preset.hotkey), !hotkeys.register(UInt32(index + 1), hotkey) { failed.append(preset.hotkey) }
            }
            if settings.readingHotkeyEnabled, let hotkey = Hotkey.parse(settings.readingHotkey),
               !hotkeys.register(AppController.readingHotkeyID, hotkey) {
                failed.append(settings.readingHotkey)
            }
        }
        refreshTexts()
        if notify && !failed.isEmpty {
            toast.show(text.t("{0} is already used by another app. Choose another hotkey in Settings.",
                              failed.map(Hotkey.display).joined(separator: ", ")), kind: .warning)
        }
        return failed
    }

    /// Recording a shortcut in Settings must not trigger the current ones.
    func suspendHotkeys(_ suspended: Bool) {
        guard hotkeysSuspended != suspended else { return }
        hotkeysSuspended = suspended
        applyHotkeys(notify: false)
    }

    func showSettings() {
        if let settingsWindow {
            // Also reached while the window is minimized; reuse it rather than opening a second one.
            settingsWindow.window?.deminiaturize(nil)
            NSApp.activate(ignoringOtherApps: true)
            settingsWindow.window?.makeKeyAndOrderFront(nil)
            requestUpdateCheck()
            return
        }
        let controller = SettingsWindowController(app: self)
        settingsWindow = controller
        controller.onClose = { [weak self, weak controller] in
            if self?.settingsWindow === controller { self?.settingsWindow = nil }
            self?.suspendHotkeys(false)
        }
        controller.present()
        requestUpdateCheck()
    }

    /// Saves and applies settings; returns the hotkeys another app already owns.
    func saveSettings(_ updated: AppSettings) throws -> [String] {
        let normalized = updated.normalized()
        try SettingsStore.save(settingsURL, normalized)
        settings = normalized
        text.setLanguage(settings.uiLanguage)
        if settings.checkForUpdates { requestUpdateCheck() }
        hotkeysSuspended = false
        return applyHotkeys(notify: false)
    }

    private func changeDisplayLanguage(_ code: String) {
        let reopen = settingsWindow?.window?.isVisible == true
        settingsWindow?.close()
        do {
            _ = try saveSettings(settings.with(\.uiLanguage, code))
        } catch {
            ErrorLog.write("Could not save settings", error)
            toast.show(text.t("Could not save settings: {0}", error.localizedDescription), kind: .error)
        }
        if reopen { showSettings() }
    }

    // MARK: Translation

    private func hotkeyPressed(_ id: UInt32) {
        guard operation == nil else { return }
        let reading = id == AppController.readingHotkeyID
        operation = Task { [weak self] in
            await self?.translateActive(presetIndex: Int(id) - 1, reading: reading)
            self?.operation = nil
        }
    }

    private func translateActive(presetIndex: Int, reading: Bool) async {
        guard !busy, !installing else { return }
        if reading ? !settings.readingHotkeyEnabled : !settings.presets.indices.contains(presetIndex) { return }
        let preset = reading ? nil : settings.presets[presetIndex]
        guard reading || preset!.enabled,
              let target = Languages.find(reading ? settings.readingTargetLanguage : preset!.targetLanguage) else { return }
        guard Accessibility.isTrusted else {
            toast.show(text.t("Allow Inline Translator in Privacy & Security > Accessibility so it can copy and paste the selected text. Click here to open it."),
                       kind: .warning, duration: 8) { Accessibility.requestTrust(); Accessibility.openSettings() }
            return
        }
        guard let front = NSWorkspace.shared.frontmostApplication else { return }
        let pid = front.processIdentifier
        let appID = front.bundleIdentifier
        let appName = front.localizedName ?? appID ?? ""
        busy = true
        bubble.hide()
        var snapshot: PasteboardSnapshot?
        var restoreClipboard = false
        var ownedClipboard = 0
        var guardian: InputGuard?
        defer {
            indicator.hide()
            if restoreClipboard, let snapshot, Pasteboard.changeCount == ownedClipboard { snapshot.restore() }
            guardian?.dispose()
            busy = false
        }
        do {
            // Do not treat the hotkey's own key presses as edits, or combine its modifiers with Cmd+C.
            await Keyboard.waitForModifiersReleased(timeout: 2)
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid else { return }
            let watcher = try InputGuard(pid: pid)
            guardian = watcher
            watcher.onCancel = { [weak self] in self?.operation?.cancel() }

            func copySelection(selectAll: Bool, wait: TimeInterval) async throws -> String? {
                guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid else { return nil }
                let before = Pasteboard.changeCount
                defer {
                    let after = Pasteboard.changeCount
                    if after != before && !watcher.inputChanged {
                        ownedClipboard = after
                        restoreClipboard = true
                    }
                }
                return try await AppController.copy(selectAll: selectAll, wait: wait)
            }

            snapshot = PasteboardSnapshot.take()
            let wholeInputApp = Capture.decide(selectionCopied: false, appID: appID, wholeInputApps: settings.wholeInputApps) == .wholeInput
            var copied = try await copySelection(selectAll: false,
                                                 wait: reading || !wholeInputApp ? AppController.copyWait : AppController.selectionProbe)
            let mode = Capture.decide(selectionCopied: !(copied ?? "").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                                      appID: appID, wholeInputApps: settings.wholeInputApps, reading: reading)
            if mode == .wholeInput {
                guard watcher.canReplace else { return }
                copied = try await copySelection(selectAll: true, wait: AppController.copyWait)
                guard let whole = copied, !whole.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                    toast.show(text.t("Could not read the input box in {0}. Drag-select the text, then press the hotkey.", appName), kind: .info)
                    return
                }
                if Capture.looksLikeWholeDocument(whole) {
                    toast.show(text.t("That looks like a whole document, not a chat message, so nothing was translated. Drag-select the part to translate, or remove {0} from the whole-input apps in Settings.", appName), kind: .warning)
                    return
                }
            }
            guard mode != .needSelection, let source = copied, !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                toast.show(text.t("Select the text to translate first, then press the hotkey."), kind: .info)
                return
            }

            watcher.captureFocus()
            let focused = Accessibility.focusedElement(pid)
            let readOnly = reading || (mode == .selection && Accessibility.isReadOnly(focused))
            let mouse = ScreenSpace.mouse
            let selection = settings.bubblePosition == "selection" && mode == .selection ? Accessibility.selectionBounds(focused) : nil
            let near = selection ?? CGRect(x: mouse.x + 12, y: mouse.y, width: 1, height: 12)
            let heading = Direction.resolve(source, target: target, home: Languages.home(settings.homeLanguage)) ?? target
            // Read off the main thread: a slow app can take up to its Accessibility timeout to answer.
            let context = settings.useContext && mode == .selection
                ? await Task.detached { Accessibility.selectionContext(focused, selected: source) }.value : nil
            try Task.checkCancellation()
            if restoreClipboard, let snapshot, Pasteboard.changeCount == ownedClipboard { snapshot.restore() }
            restoreClipboard = false
            indicator.show(near: near, text: context == nil ? text.t("Translating to {0}… · Esc to cancel", heading.nativeName)
                : text.t("Translating to {0} with context… · Esc to cancel", heading.nativeName))
            let result = try await translator().translate(settings, target: target, text: source, context: context)
            try Task.checkCancellation()
            indicator.hide()
            signInFailures.remove(result.engine.provider)
            if let primaryError = result.primaryError {
                if EngineCli.isSignInError(primaryError) { signInFailures.insert(settings.primary.provider) }
                ErrorLog.write("\(settings.primary.displayName) failed, used \(result.engine.displayName)", primaryError)
                if EngineCli.isSignInError(primaryError), signInReminded.insert(settings.primary.provider).inserted {
                    notifySignIn(settings.primary.provider, text.t("{0} needs you to sign in again, so {1} translated this. Click here to sign in to {0}.",
                                                                   EngineCli.productName(settings.primary.provider),
                                                                   EngineCli.productName(result.engine.provider)))
                }
            }

            func showBubble(changed: Bool = false) {
                bubble.show(near: near, title: "→ \((result.output ?? target).nativeName) · \(result.engine.displayName)",
                            body: result.text,
                            hint: changed ? text.t("Input changed · translation shown without replacing text")
                                : text.t("Translation · press any key or click to close"),
                            dark: settings.bubbleTheme == "dark", followMouse: settings.bubblePosition == "mouse")
            }

            if readOnly { showBubble(); return }
            guard watcher.canReplace else { showBubble(changed: true); return }

            // Verify the original selection, including whole-input selections. Never select all again:
            // doing so could overwrite text typed while the engine was working.
            snapshot = PasteboardSnapshot.take()
            let current = try await copySelection(selectAll: false, wait: AppController.copyWait)
            guard watcher.canReplace, Capture.selectionUnchanged(source, current) else { showBubble(changed: true); return }
            try Task.checkCancellation()
            ownedClipboard = Pasteboard.setPrivate(result.text)
            restoreClipboard = true
            guard watcher.canReplace else { showBubble(changed: true); return }
            watcher.finishTranslation()
            Keyboard.chord(KeyCode.v)
            try await Task.sleep(nanoseconds: AppController.pasteSettle)
            // Many apps do not report read-only text. If the original selection is still selected after
            // pasting, the paste was ignored, so show the translation instead.
            if mode == .selection && watcher.canReplace {
                let afterPaste = try await copySelection(selectAll: false, wait: AppController.selectionProbe)
                if Capture.pasteWasIgnored(source, afterPaste) { showBubble() }
            }
        } catch is CancellationError {
            // User cancellation is not an engine failure and must not start the fallback or log an error.
        } catch let failure as TranslationFailed {
            ErrorLog.write(settings.primary.displayName + " failed", failure.primary)
            if let fallback = failure.fallback { ErrorLog.write(settings.fallback.displayName + " failed", fallback) }
            let provider: Provider = EngineCli.isSignInError(failure.primary) ? settings.primary.provider
                : failure.fallback.map(EngineCli.isSignInError) == true ? settings.fallback.provider : .none
            if EngineCli.isSignInError(failure.primary) { signInFailures.insert(settings.primary.provider) }
            if let fallback = failure.fallback, EngineCli.isSignInError(fallback) { signInFailures.insert(settings.fallback.provider) }
            if provider != .none {
                notifySignIn(provider, text.t("{0} needs you to sign in. Click here to sign in.", EngineCli.productName(provider)))
            } else {
                toast.show(text.t("Translation failed: {0}", failure.localizedDescription), kind: .error)
            }
        } catch {
            // Last line of defence for a hotkey handler: report instead of failing silently.
            ErrorLog.write("Translation hotkey failed", error)
            toast.show(text.t("Translation failed: {0}", error.localizedDescription), kind: .error)
        }
    }

    private static func copy(selectAll: Bool, wait: TimeInterval) async throws -> String? {
        try Task.checkCancellation()
        let before = Pasteboard.changeCount
        if selectAll { Keyboard.chord(KeyCode.a) }
        Keyboard.chord(KeyCode.c)
        let start = Date()
        while true {
            try Task.checkCancellation()
            // PDF viewers can clear the pasteboard before publishing the selected text,
            // so a changed count alone does not mean the copy has finished.
            if Pasteboard.changeCount != before, let copied = Pasteboard.readText(),
               !copied.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                return copied
            }
            if Date().timeIntervalSince(start) > wait { return nil }
            try await Task.sleep(nanoseconds: 15_000_000)
        }
    }

    func translator() -> Translator {
        let environment = environment
        return Translator { engine, instructions, text in
            try await EngineCli.translate(engine, instructions: instructions, text: text, environment: environment)
        }
    }

    func startSignIn(_ provider: Provider) throws {
        try SignInTerminal.open(provider, environment: environment)
        signInReminded.remove(provider)
        signInFailures.remove(provider)
    }

    /// Results of the engine test in Settings count like translations.
    func noteEngineResult(_ provider: Provider, signInFailed: Bool?) {
        guard let signInFailed else { return }
        if signInFailed { signInFailures.insert(provider) } else { signInFailures.remove(provider) }
    }

    private func notifySignIn(_ provider: Provider, _ message: String) {
        toast.show(message, kind: .warning, duration: 8) { [weak self] in
            guard let self else { return }
            do { try startSignIn(provider) } catch {
                ErrorLog.write("Could not start sign-in", error)
                showSettings()
            }
        }
    }

    // MARK: Updates

    @discardableResult
    func requestUpdateCheck(manual: Bool = false) -> Task<Void, Never> {
        if checkingUpdates, let currentUpdateCheck {
            settingsWindow?.model.setUpdateStatus(text.t("Checking for updates…"), checking: true)
            return currentUpdateCheck
        }
        let task = Task { await checkForUpdate(manual: manual) }
        currentUpdateCheck = task
        return task
    }

    private func checkForUpdate(manual: Bool) async {
        if (!manual && !settings.checkForUpdates) || installing {
            if manual { settingsWindow?.model.clearUpdateStatus() }
            return
        }
        checkingUpdates = true
        refreshTexts()
        settingsWindow?.model.setUpdateStatus(text.t("Checking for updates…"), checking: true)
        defer {
            checkingUpdates = false
            refreshTexts()
        }
        do {
            let latest = try await UpdateChecker.fetchLatest()
            showUpdate(latest.flatMap { $0.version > UpdateChecker.current ? $0 : nil })
            let message = update.map { text.t("Update available: {0}.", $0.tag) }
                ?? text.t("You are using the latest version ({0}).", UpdateChecker.current.description)
            settingsWindow?.model.setUpdateStatus(message)
            if manual && settingsWindow == nil { toast.show(message, kind: .info) }
        } catch {
            ErrorLog.write("Update check failed", error)
            let message = text.t("Update check failed: {0}", error.localizedDescription)
            settingsWindow?.model.setUpdateStatus(message, error: true)
            if manual && settingsWindow == nil { toast.show(message, kind: .warning) }
        }
    }

    private func showUpdate(_ release: ReleaseInfo?) {
        let isNew = release != nil && release?.tag != update?.tag
        update = release
        settingsWindow?.model.update = release
        refreshTexts()
        if let release, isNew {
            toast.show(text.t("Inline Translator {0} is available. Click the menu bar icon and choose Update.", release.tag), kind: .info)
        }
    }

    func installUpdate() async {
        guard let update, !installing else { return }
        guard settings.autoInstallUpdates, let bundle = UpdateInstaller.installableBundle(), update.packageURL != nil,
              let bundleID = Bundle.main.bundleIdentifier else {
            NSWorkspace.shared.open(update.url)
            return
        }
        installing = true
        refreshTexts()
        defer {
            installing = false
            refreshTexts()
        }
        do {
            toast.show(text.t("Updating to {0}…", update.tag), kind: .info)
            try await UpdateInstaller.install(update, into: bundle, bundleID: bundleID)
            Relauncher.relaunch(bundle)
            NSApp.terminate(nil)
        } catch {
            ErrorLog.write("Update failed", error)
            let page = update.url
            toast.show(text.t("Automatic update failed: {0}", error.localizedDescription), kind: .error, duration: 8) {
                NSWorkspace.shared.open(page)
            }
        }
    }

    // MARK: Menu actions

    @objc private func allowAccessibility() {
        Accessibility.requestTrust()
        Accessibility.openSettings()
    }

    @objc private func installUpdateFromMenu() { Task { await installUpdate() } }
    @objc private func checkUpdatesFromMenu() { requestUpdateCheck(manual: true) }
    @objc private func showSettingsFromMenu() { showSettings() }
    @objc private func quit() { NSApp.terminate(nil) }

    @objc private func togglePause() {
        paused.toggle()
        applyHotkeys(notify: true)
    }

    @objc private func chooseDisplayLanguage(_ sender: NSMenuItem) {
        changeDisplayLanguage(sender.representedObject as? String ?? "")
    }
}

enum SettingsLanguages {
    static let display: [(code: String, name: String)] = [("", "System default"), ("ko", "한국어"), ("en", "English"),
                                                           ("zh-CN", "简体中文"), ("ja", "日本語")]

    static func label(_ text: UiText) -> String {
        text.language == "en" ? text.t("Display language") : text.t("Display language") + " (Language)"
    }
}

enum MainMenu {
    /// A menu bar app still needs the standard Edit and Window commands for text fields and Cmd+W in Settings.
    static func build() -> NSMenu {
        let main = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Quit Inline Translator", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)
        let editItem = NSMenuItem()
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)
        let windowItem = NSMenuItem()
        let window = NSMenu(title: "Window")
        window.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = window
        main.addItem(windowItem)
        return main
    }
}
