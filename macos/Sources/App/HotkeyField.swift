import AppKit
import Carbon.HIToolbox
import SwiftUI

/// Click, then press a key combination. Delete, Backspace or Esc clear it; Tab moves on.
struct HotkeyField: NSViewRepresentable {
    @Binding var value: String
    let placeholder: String
    let enabled: Bool
    let onFocus: (Bool) -> Void

    func makeNSView(context: Context) -> RecorderView {
        let view = RecorderView()
        view.onChange = { value = $0 }
        view.onFocus = onFocus
        return view
    }

    func updateNSView(_ view: RecorderView, context: Context) {
        view.onChange = { value = $0 }
        view.onFocus = onFocus
        view.update(value: value, placeholder: placeholder, enabled: enabled)
    }
}

final class RecorderView: NSView {
    var onChange: (String) -> Void = { _ in }
    var onFocus: (Bool) -> Void = { _ in }
    private let label = NSTextField(labelWithString: "")
    private var monitor: Any?
    private var value = ""
    private var placeholder = ""
    private var enabled = true
    private var recording = false { didSet { refresh() } }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.cornerRadius = 6
        layer?.borderWidth = 1
        label.alignment = .center
        label.font = .systemFont(ofSize: 13, weight: .semibold)
        label.translatesAutoresizingMaskIntoConstraints = false
        addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 8),
            label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            label.centerYAnchor.constraint(equalTo: centerYAnchor),
            heightAnchor.constraint(equalToConstant: 26),
            widthAnchor.constraint(greaterThanOrEqualToConstant: 160),
        ])
        setAccessibilityElement(true)
        setAccessibilityRole(.button)
    }

    required init?(coder: NSCoder) { fatalError() }

    func update(value: String, placeholder: String, enabled: Bool) {
        self.value = value
        self.placeholder = placeholder
        self.enabled = enabled
        refresh()
    }

    private func refresh() {
        let shown = recording ? placeholder : value.isEmpty ? placeholder : Hotkey.display(value)
        label.stringValue = shown
        label.textColor = enabled && !(value.isEmpty && !recording) ? .labelColor : .secondaryLabelColor
        layer?.borderColor = (recording ? NSColor.controlAccentColor : NSColor.separatorColor).cgColor
        layer?.backgroundColor = NSColor.controlBackgroundColor.cgColor
        setAccessibilityValue(value.isEmpty ? placeholder : Hotkey.display(value))
    }

    // Only a deliberate click or accessibility press starts recording; otherwise the window would focus the
    // first field on open, pause the global hotkeys and record whatever key comes next.
    private var armed = false
    override var acceptsFirstResponder: Bool { armed }
    override var canBecomeKeyView: Bool { false }

    override func mouseDown(with event: NSEvent) { startRecording() }

    override func accessibilityPerformPress() -> Bool { startRecording() }

    @discardableResult
    private func startRecording() -> Bool {
        armed = true
        if window?.makeFirstResponder(self) == true { return true }
        armed = false
        return false
    }

    override func becomeFirstResponder() -> Bool {
        recording = true
        onFocus(true)
        // A local monitor sees every key before menus and default buttons, so Esc and Cmd shortcuts can be recorded.
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.window?.firstResponder === self else { return event }
            return self.record(event) ? nil : event
        }
        return true
    }

    override func resignFirstResponder() -> Bool {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        armed = false
        recording = false
        onFocus(false)
        return true
    }

    private func record(_ event: NSEvent) -> Bool {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let code = UInt32(event.keyCode)
        if code == UInt32(kVK_Tab) && flags.isDisjoint(with: [.command, .control, .option]) { return false }
        if flags.isDisjoint(with: [.command, .control, .option, .shift]),
           [kVK_Delete, kVK_ForwardDelete, kVK_Escape].map(UInt32.init).contains(code) {
            onChange("")
            window?.makeFirstResponder(nil)
            return true
        }
        var modifiers: HotkeyModifiers = []
        if flags.contains(.control) { modifiers.insert(.control) }
        if flags.contains(.option) { modifiers.insert(.option) }
        if flags.contains(.shift) { modifiers.insert(.shift) }
        if flags.contains(.command) { modifiers.insert(.command) }
        let candidate = Hotkey(modifiers: modifiers, keyCode: code)
        if Hotkey.isSupportedKey(code) {
            onChange(candidate.description)
        } else {
            // Keep what was pressed visible so the inline error can explain it.
            let key = event.charactersIgnoringModifiers?.uppercased() ?? "?"
            onChange(String(candidate.symbols.dropLast()) + key)
        }
        if candidate.isValid { window?.makeFirstResponder(nil) }
        return true
    }
}
