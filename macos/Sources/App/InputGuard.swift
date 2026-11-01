import AppKit
import ApplicationServices

/// Watches the user's input while a translation runs, without blocking it. Esc is held back only while
/// the engine works (it cancels the translation); everything else passes through unchanged.
final class InputGuard {
    private struct FocusState {
        let pid: pid_t
        let element: AXUIElement?
        let window: AXUIElement?

        static func read(_ pid: pid_t) -> FocusState? {
            guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid else { return nil }
            return FocusState(pid: pid, element: Accessibility.focusedElement(pid), window: Accessibility.focusedWindow(pid))
        }

        func matches(_ other: FocusState?) -> Bool {
            guard let other else { return false }
            return pid == other.pid && same(element, other.element) && same(window, other.window)
        }

        private func same(_ first: AXUIElement?, _ second: AXUIElement?) -> Bool {
            switch (first, second) {
            case (nil, nil): true
            case let (first?, second?): CFEqual(first, second)
            default: false
            }
        }
    }

    private let pid: pid_t
    private var focus: FocusState?
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private var escapeActive = true
    private(set) var inputChanged = false
    private(set) var cancelled = false
    var onCancel: (() -> Void)?

    init(pid: pid_t) throws {
        self.pid = pid
        focus = FocusState.read(pid)
        let mask = [CGEventType.keyDown, .leftMouseDown, .rightMouseDown, .otherMouseDown]
            .reduce(CGEventMask(0)) { $0 | (1 << $1.rawValue) }
        let callback: CGEventTapCallBack = { _, type, event, context in
            let guardian = Unmanaged<InputGuard>.fromOpaque(context!).takeUnretainedValue()
            return guardian.handle(type, event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                          eventsOfInterest: mask, callback: callback,
                                          userInfo: Unmanaged.passUnretained(self).toOpaque()) else {
            throw EngineError(message: "Could not watch the keyboard to allow Esc to cancel. Check the Accessibility permission.")
        }
        self.tap = tap
        source = CFMachPortCreateRunLoopSource(nil, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }

    var canReplace: Bool {
        focus != nil && !inputChanged && !cancelled && focus!.matches(FocusState.read(pid))
    }

    /// Records the focus after copying; a different focused element means the user moved on.
    func captureFocus() {
        let current = FocusState.read(pid)
        if focus?.matches(current) != true { inputChanged = true }
        focus = current
    }

    /// Returns Esc to the foreground app once the result is about to be pasted.
    func finishTranslation() { escapeActive = false }

    func dispose() {
        if let tap { CGEvent.tapEnable(tap: tap, enable: false) }
        if let source { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        tap = nil
        source = nil
    }

    deinit { dispose() }

    private func handle(_ type: CGEventType, _ event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        let userData = event.getIntegerValueField(.eventSourceUserData)
        if type == .keyDown, escapeActive, userData != Capture.syntheticInputTag,
           event.getIntegerValueField(.keyboardEventKeycode) == Int64(KeyCode.escape) {
            if !cancelled {
                cancelled = true
                onCancel?()
            }
            return nil
        }
        if Capture.inputInvalidates(type, userData: userData) { inputChanged = true }
        return Unmanaged.passUnretained(event)
    }
}
