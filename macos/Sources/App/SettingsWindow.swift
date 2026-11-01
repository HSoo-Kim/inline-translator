import AppKit
import SwiftUI

@MainActor
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    let model: SettingsModel
    var onClose: () -> Void = {}
    private var timer: Timer?

    init(app: AppController) {
        model = SettingsModel(app: app)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 640, height: 700),
                              styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        window.title = "Inline Translator – " + app.text.t("Settings")
        window.contentMinSize = NSSize(width: 520, height: 420)
        window.isReleasedWhenClosed = false
        window.setFrameAutosaveName("InlineTranslatorSettings")
        super.init(window: window)
        window.contentViewController = NSHostingController(rootView: SettingsView(model: model))
        window.delegate = self
        model.close = { [weak self] in self?.close() }
    }

    required init?(coder: NSCoder) { fatalError() }

    func present() {
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        model.refreshSignIn()
        // Picks up Accessibility permission granted in System Settings while this window is open.
        timer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.model.refreshAccessibility() }
        }
    }

    func windowDidBecomeKey(_ notification: Notification) {
        // Returning from a Terminal sign-in shows the new account state.
        if !model.signIn.isEmpty { model.refreshSignIn() }
        model.refreshAccessibility()
    }

    func windowWillClose(_ notification: Notification) {
        timer?.invalidate()
        timer = nil
        onClose()
    }
}

struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    private var text: UiText { model.text }

    var body: some View {
        VStack(spacing: 0) {
            if !model.accessibilityTrusted {
                HStack(alignment: .top, spacing: 12) {
                    Image(systemName: "hand.raised.fill").foregroundColor(.orange).font(.title2)
                    VStack(alignment: .leading, spacing: 6) {
                        Text(text.t("Allow Accessibility")).font(.headline)
                        Text(text.t("Inline Translator needs Accessibility permission to copy the selected text and paste the translation. Turn on Inline Translator in System Settings > Privacy & Security > Accessibility."))
                            .font(.callout).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
                        Button(text.t("Open System Settings")) { model.allowAccessibility() }
                    }
                    Spacer(minLength: 0)
                }
                .padding(14)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color.orange.opacity(0.12)))
                .padding([.horizontal, .top], 16)
                .accessibilityElement(children: .contain)
                .accessibilityIdentifier("accessibility-banner")
            }
            TabView(selection: $model.tab) {
                TranslationTab(model: model).tabItem { Text(text.t("Translation")) }.tag("translation")
                EnginesTab(model: model).tabItem { Text(text.t("Engines")) }.tag("engines")
                GeneralTab(model: model).tabItem { Text(text.t("General")) }.tag("general")
            }
            .padding(.horizontal, 12)
            .padding(.top, 12)
            if let error = model.saveError {
                Text(error).foregroundColor(.red).frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 20).padding(.top, 8)
            }
            Divider().padding(.top, 8)
            HStack(spacing: 8) {
                Link("v\(UpdateChecker.current.description) · GitHub", destination: URL(string: "https://github.com/HSoo-Kim/inline-translator")!)
                    .foregroundColor(.secondary)
                Spacer()
                if let update = model.update {
                    Button(text.t("Update")) { model.installUpdate() }.help(text.t("Update to {0}", update.tag))
                }
                Button(text.t("Cancel")) { model.close() }.keyboardShortcut(.cancelAction)
                Button(text.t("Save")) { model.save() }.keyboardShortcut(.defaultAction).disabled(!model.canSave)
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 12)
        }
        .frame(minWidth: 520, minHeight: 420)
    }
}

private struct ErrorText: View {
    let message: String?
    var body: some View {
        if let message { Text(message).font(.callout).foregroundColor(.red).fixedSize(horizontal: false, vertical: true) }
    }
}

private struct Note: View {
    let message: String
    var body: some View { Text(message).font(.callout).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true) }
}

private struct LanguagePicker: View {
    let title: String
    @Binding var selection: String
    var body: some View {
        Picker(title, selection: $selection) {
            ForEach(Languages.all, id: \.code) { Text($0.label).tag($0.code) }
        }
    }
}

private struct TranslationTab: View {
    @ObservedObject var model: SettingsModel
    private var text: UiText { model.text }

    var body: some View {
        Form {
            Section {
                Picker(text.t("My language"), selection: $model.draft.homeLanguage) {
                    Text(text.t("System default") + " · " + Languages.system().nativeName).tag("")
                    ForEach(Languages.all, id: \.code) { Text($0.label).tag($0.code) }
                }
                Note(message: text.t("Your language and the hotkey language translate in both directions."))
                Picker(text.t("Tone"), selection: $model.draft.tone) {
                    Text(text.t("Match the original")).tag("natural")
                    Text(text.t("Casual")).tag("casual")
                    Text(text.t("Polite")).tag("formal")
                }
            } header: { Text(text.t("Languages and tone")) }

            Section {
                ForEach(0..<AppSettings.presetCount, id: \.self) { index in
                    Toggle(text.t("Writing hotkey {0}", index + 1), isOn: $model.draft.presets[index].enabled)
                    LabeledContent(text.t("Hotkey")) {
                        HotkeyField(value: $model.draft.presets[index].hotkey, placeholder: text.t("Press a key combination"),
                                    enabled: model.draft.presets[index].enabled, onFocus: model.suspendHotkeys)
                            .frame(width: 200)
                            .accessibilityLabel(text.t("Writing hotkey {0}", index + 1))
                    }
                    ErrorText(message: model.hotkeyError(index))
                    LanguagePicker(title: text.t("Target language"), selection: $model.draft.presets[index].targetLanguage)
                        .disabled(!model.draft.presets[index].enabled)
                }
            } header: {
                VStack(alignment: .leading, spacing: 4) {
                    Text(text.t("Hotkeys and target languages"))
                    Text(text.t("Select text and press a hotkey: it is replaced with the translation in place.")).font(.callout).foregroundColor(.secondary)
                }
            }

            Section {
                Toggle(text.t("Read-only translation hotkey"), isOn: $model.draft.readingHotkeyEnabled)
                LabeledContent(text.t("Hotkey")) {
                    HotkeyField(value: $model.draft.readingHotkey, placeholder: text.t("Press a key combination"),
                                enabled: model.draft.readingHotkeyEnabled, onFocus: model.suspendHotkeys)
                        .frame(width: 200)
                        .accessibilityLabel(text.t("Read-only translation hotkey"))
                }
                ErrorText(message: model.hotkeyError(AppSettings.presetCount))
                LanguagePicker(title: text.t("Target language"), selection: $model.draft.readingTargetLanguage)
                    .disabled(!model.draft.readingHotkeyEnabled)
                Note(message: text.t("Text in My language goes to the selected language; other text comes to My language. The original stays unchanged."))
            } header: { Text(text.t("Read-only translation hotkey")) }
        }
        .formStyle(.grouped)
    }
}

private struct EnginesTab: View {
    @ObservedObject var model: SettingsModel
    private var text: UiText { model.text }

    var body: some View {
        Form {
            EngineSection(model: model, slot: .primary, title: text.t("Primary"))
            EngineSection(model: model, slot: .fallback, title: text.t("If it fails"))
            Section {
                ForEach([Provider.claude, .codex], id: \.self) { provider in
                    let state = model.signIn[provider]
                    HStack {
                        Circle().fill(color(state)).frame(width: 8, height: 8)
                        Text(EngineCli.productName(provider) + " · " + status(state))
                            .help(model.executablePath(provider) ?? EngineCli.productName(provider) + ": " + text.t("not found"))
                        Spacer()
                        if state == .signedOut || state == .notInstalled || state == .unknown {
                            Button(state == .notInstalled ? text.t("How to install") : text.t("Sign in")) { model.accountAction(provider) }
                        }
                    }
                }
            } header: {
                VStack(alignment: .leading, spacing: 4) {
                    Text(text.t("Accounts"))
                    Text(text.t("Uses the Claude Code or Codex you are already signed in to on this Mac. No API key needed."))
                        .font(.callout).foregroundColor(.secondary)
                }
            }
        }
        .formStyle(.grouped)
    }

    private func color(_ state: SignIn?) -> Color {
        switch state {
        case .signedIn: Color(Palette.green)
        case .signedOut: Color(Palette.red)
        default: Color(Palette.gray)
        }
    }

    private func status(_ state: SignIn?) -> String { signInStatus(text, state) }
}

private func signInStatus(_ text: UiText, _ state: SignIn?) -> String {
    switch state {
    case .signedIn: text.t("Signed in")
    case .signedOut: text.t("Sign-in needed")
    case .notInstalled: text.t("Not installed")
    case .unknown: text.t("Status unknown")
    case nil: text.t("Checking…")
    }
}

private struct EngineSection: View {
    @ObservedObject var model: SettingsModel
    let slot: EngineSlot
    let title: String
    @State private var askingModel = false
    @State private var customModel = ""
    private static let customTag = "\u{1}custom"
    private var text: UiText { model.text }

    var body: some View {
        let engine = model.engine(slot)
        let options = EngineCli.suggestedModels(engine.provider)
        Section {
            if engine.provider != .none {
                let state = model.signIn[engine.provider]
                Text(signInStatus(text, state)).foregroundColor(state == .signedIn ? Color(Palette.greenInk) : state == .signedOut ? .red : .secondary)
            }
            Picker(text.t("Provider"), selection: Binding(get: { engine.provider }, set: { provider in
                model.setEngine(slot, EngineChoice(provider: provider, model: AppSettings.defaultModel(provider)))
            })) {
                if slot == .fallback { Text(text.t("None")).tag(Provider.none) }
                Text("Claude Code").tag(Provider.claude)
                Text("Codex").tag(Provider.codex)
            }
            Picker(text.t("Model"), selection: Binding(get: { engine.model }, set: { value in
                if value == EngineSection.customTag {
                    customModel = ""
                    askingModel = true
                } else {
                    model.setEngine(slot, EngineChoice(provider: engine.provider, model: value))
                }
            })) {
                ForEach(options, id: \.id) { Text($0.label).tag($0.id) }
                if engine.provider != .none && !options.contains(where: { $0.id == engine.model }) {
                    Text(engine.model).tag(engine.model)
                }
                Divider()
                Text(text.t("Enter a model name…")).tag(EngineSection.customTag)
            }
            .disabled(engine.provider == .none)
            HStack(alignment: .top) {
                Button(text.t("Test")) { model.test(slot) }
                    .disabled(engine.provider == .none || model.tests[slot]?.state == .running)
                if let test = model.tests[slot] {
                    Text(test.message)
                        .foregroundColor(test.state == .succeeded ? Color(Palette.greenInk) : test.state == .failed ? .red : .secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                }
            }
        } header: { Text(title) }
        .sheet(isPresented: $askingModel) {
            VStack(alignment: .leading, spacing: 12) {
                Text(text.t("Model")).font(.headline)
                Text(text.t("Type the exact model name, for example claude-opus-5-5.")).foregroundColor(.secondary)
                TextField("", text: $customModel).frame(width: 320)
                HStack {
                    Spacer()
                    Button(text.t("Cancel")) { askingModel = false }.keyboardShortcut(.cancelAction)
                    Button(text.t("OK")) {
                        let id = ModelOption.resolve(options, customModel)
                        if AppSettings.isValidModel(id) { model.setEngine(slot, EngineChoice(provider: engine.provider, model: id)) }
                        askingModel = false
                    }
                    .keyboardShortcut(.defaultAction)
                    .disabled(!AppSettings.isValidModel(ModelOption.resolve(options, customModel)))
                }
            }
            .padding(20)
        }
    }
}

private struct GeneralTab: View {
    @ObservedObject var model: SettingsModel
    @State private var pickingApps = false
    private var text: UiText { model.text }

    var body: some View {
        Form {
            Section {
                FlowLayout(spacing: 6) {
                    ForEach(model.draft.wholeInputApps, id: \.self) { id in
                        HStack(spacing: 4) {
                            Text(AppDiscovery.name(for: id))
                            Button {
                                model.draft.wholeInputApps.removeAll { $0 == id }
                            } label: { Image(systemName: "xmark.circle.fill") }
                                .buttonStyle(.plain)
                                .foregroundColor(.secondary)
                                .accessibilityLabel(text.t("Remove {0}", AppDiscovery.name(for: id)))
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 4)
                        .background(Capsule().fill(Color.secondary.opacity(0.12)))
                        .help(id)
                    }
                    Button(text.t("+ Add apps")) { pickingApps = true }
                }
            } header: {
                VStack(alignment: .leading, spacing: 4) {
                    Text(text.t("Apps that translate the whole input box"))
                    Text(text.t("In these apps, pressing the hotkey with nothing selected translates the whole message. Drag-select first to translate only part of it."))
                        .font(.callout).foregroundColor(.secondary)
                }
            }

            Section {
                Picker(text.t("Appearance"), selection: $model.draft.bubbleTheme) {
                    Text(text.t("White background, black text")).tag("light")
                    Text(text.t("Black background, white text")).tag("dark")
                }
                Picker(text.t("Bubble position"), selection: $model.draft.bubblePosition) {
                    Text(text.t("Follows the mouse")).tag("mouse")
                    Text(text.t("Right above or below the selected text")).tag("selection")
                }
            } header: { Text(text.t("Read-only translation bubble")) }

            Section {
                Picker(SettingsLanguages.label(text), selection: $model.draft.uiLanguage) {
                    ForEach(SettingsLanguages.display, id: \.code) { language in
                        Text(language.code.isEmpty ? text.t("System default") : language.name).tag(language.code)
                    }
                }
                Note(message: text.t("Display language changes after Save. Reopen Settings to see it."))
                Toggle(text.t("Open at login"), isOn: $model.openAtLogin).disabled(!LoginItem.isAvailable)
                Toggle(text.t("Check for new versions"), isOn: $model.draft.checkForUpdates)
                Toggle(text.t("Install automatically when Update is clicked"), isOn: $model.draft.autoInstallUpdates)
                    .disabled(!model.draft.checkForUpdates)
                HStack {
                    Button(text.t("Check for updates")) { model.checkUpdates() }.disabled(model.checkingUpdates)
                    if let status = model.updateStatus {
                        Text(status.message).foregroundColor(status.error ? .red : .secondary).fixedSize(horizontal: false, vertical: true)
                    }
                }
            } header: { Text(text.t("General")) }
        }
        .formStyle(.grouped)
        .sheet(isPresented: $pickingApps) {
            AppPicker(text: text, selected: model.draft.wholeInputApps) { model.draft.wholeInputApps = $0 }
        }
    }
}

/// Wraps chips onto as many lines as the width needs.
struct FlowLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(width: proposal.width ?? .infinity, subviews: subviews)
        return CGSize(width: proposal.width ?? rows.map(\.width).max() ?? 0, height: rows.last.map { $0.y + $0.height } ?? 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for row in arrange(width: bounds.width, subviews: subviews) {
            for (index, x) in zip(row.indices, row.xs) {
                subviews[index].place(at: CGPoint(x: bounds.minX + x, y: bounds.minY + row.y), proposal: .unspecified)
            }
        }
    }

    private struct Row { var indices: [Int] = []; var xs: [CGFloat] = []; var y: CGFloat = 0; var width: CGFloat = 0; var height: CGFloat = 0 }

    private func arrange(width: CGFloat, subviews: Subviews) -> [Row] {
        var rows: [Row] = [Row()]
        for (index, subview) in subviews.enumerated() {
            let size = subview.sizeThatFits(.unspecified)
            if !rows[rows.count - 1].indices.isEmpty && rows[rows.count - 1].width + spacing + size.width > width {
                let last = rows[rows.count - 1]
                rows.append(Row(y: last.y + last.height + spacing))
            }
            var row = rows.removeLast()
            let x = row.indices.isEmpty ? 0 : row.width + spacing
            row.indices.append(index)
            row.xs.append(x)
            row.width = x + size.width
            row.height = max(row.height, size.height)
            rows.append(row)
        }
        return rows
    }
}

private struct AppPicker: View {
    let text: UiText
    let apply: ([String]) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var selected: [String]
    @State private var search = ""
    @State private var apps: [AppEntry] = []

    init(text: UiText, selected: [String], apply: @escaping ([String]) -> Void) {
        self.text = text
        self.apply = apply
        _selected = State(initialValue: selected)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(text.t("Choose apps")).font(.headline)
            Text(text.t("Pick the chat apps where pressing the hotkey without selecting should translate the whole message."))
                .foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
            TextField(text.t("Search apps"), text: $search).textFieldStyle(.roundedBorder)
            List(visible) { app in
                Toggle(isOn: Binding(get: { isSelected(app) }, set: { toggle(app, $0) })) {
                    HStack(spacing: 8) {
                        if let icon = AppDiscovery.icon(for: app.bundleID) {
                            Image(nsImage: icon).resizable().frame(width: 20, height: 20)
                        }
                        VStack(alignment: .leading, spacing: 1) {
                            Text(app.name)
                            Text(app.bundleID).font(.caption).foregroundColor(.secondary)
                        }
                        Spacer()
                        if AppCatalog.isKnownChatApp(app.bundleID) { Badge(label: text.t("Chat app")) }
                        if app.running { Badge(label: text.t("Running")) }
                    }
                }
                .toggleStyle(.checkbox)
            }
            .frame(minHeight: 320)
            HStack {
                Spacer()
                Button(text.t("Cancel")) { dismiss() }.keyboardShortcut(.cancelAction)
                Button(text.t("Done")) {
                    apply(selected)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 520, height: 520)
        .onAppear { apps = mergedWithSelection(AppDiscovery.discover()) }
    }

    private var visible: [AppEntry] {
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return apps }
        return apps.filter { $0.name.localizedCaseInsensitiveContains(query) || $0.bundleID.localizedCaseInsensitiveContains(query) }
    }

    /// Selected apps that are not installed stay listed so they can be removed here too.
    private func mergedWithSelection(_ discovered: [AppEntry]) -> [AppEntry] {
        let known = Set(discovered.map(\.id))
        let missing = selected.filter { !known.contains($0.lowercased()) }
            .map { AppEntry(bundleID: $0, name: AppDiscovery.name(for: $0), path: nil, running: false) }
        return discovered + missing
    }

    private func isSelected(_ app: AppEntry) -> Bool { selected.contains { $0.caseInsensitiveCompare(app.bundleID) == .orderedSame } }

    private func toggle(_ app: AppEntry, _ on: Bool) {
        selected.removeAll { $0.caseInsensitiveCompare(app.bundleID) == .orderedSame }
        if on { selected.append(app.bundleID) }
    }
}

private struct Badge: View {
    let label: String
    var body: some View {
        Text(label).font(.caption).padding(.horizontal, 6).padding(.vertical, 2)
            .background(Capsule().fill(Color.accentColor.opacity(0.15)))
    }
}
