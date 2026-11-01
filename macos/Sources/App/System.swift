import AppKit
import ApplicationServices
import Carbon.HIToolbox

enum Accessibility {
    static var isTrusted: Bool { AXIsProcessTrusted() }

    /// Shows the system prompt that adds this app to Privacy & Security > Accessibility.
    static func requestTrust() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    static func openSettings() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
    }

    static func application(_ pid: pid_t) -> AXUIElement {
        let element = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(element, 0.5)
        return element
    }

    static func attribute(_ element: AXUIElement, _ name: String) -> AnyObject? {
        var value: AnyObject?
        return AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success ? value : nil
    }

    static func focusedElement(_ pid: pid_t) -> AXUIElement? {
        attribute(application(pid), kAXFocusedUIElementAttribute).map { $0 as! AXUIElement }
    }

    static func focusedWindow(_ pid: pid_t) -> AXUIElement? {
        attribute(application(pid), kAXFocusedWindowAttribute).map { $0 as! AXUIElement }
    }

    /// Read-only only when the app says so; anything unknown counts as editable, so pasting stays the default.
    static func isReadOnly(_ element: AXUIElement?) -> Bool {
        guard let element, let role = attribute(element, kAXRoleAttribute) as? String else { return false }
        return role == kAXStaticTextRole || role == "AXWebArea"
    }

    /// Screen rectangle of the selected text (top-left origin), when the app exposes it.
    static func selectionBounds(_ element: AXUIElement?) -> CGRect? {
        guard let element, let range = attribute(element, kAXSelectedTextRangeAttribute) else { return nil }
        var bounds: AnyObject?
        guard AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute as CFString,
                                                         range, &bounds) == .success, let bounds else { return nil }
        var rect = CGRect.zero
        guard AXValueGetValue(bounds as! AXValue, .cgRect, &rect), rect.width > 0, rect.height > 0 else { return nil }
        return rect
    }
}

/// Converts between AppKit screen coordinates (bottom-left origin) and the top-left origin used by
/// Accessibility and `BubbleLayout`.
enum ScreenSpace {
    static var primaryHeight: CGFloat { NSScreen.screens.first?.frame.height ?? 0 }

    static func topLeft(_ rect: CGRect) -> CGRect {
        CGRect(x: rect.minX, y: primaryHeight - rect.maxY, width: rect.width, height: rect.height)
    }

    static func appKit(_ rect: CGRect) -> CGRect { topLeft(rect) }

    static var mouse: CGPoint {
        let point = NSEvent.mouseLocation
        return CGPoint(x: point.x, y: primaryHeight - point.y)
    }

    /// Visible frame (without menu bar and Dock) of the screen holding the point, in top-left coordinates.
    static func visibleArea(near point: CGPoint) -> CGRect {
        let appKitPoint = CGPoint(x: point.x, y: primaryHeight - point.y)
        let screen = NSScreen.screens.first { NSMouseInRect(appKitPoint, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens[0]
        return topLeft(screen.visibleFrame)
    }
}

enum Keyboard {
    static func chord(_ key: Int, flags: CGEventFlags = .maskCommand) {
        let source = CGEventSource(stateID: .hidSystemState)
        for down in [true, false] {
            guard let event = CGEvent(keyboardEventSource: source, virtualKey: CGKeyCode(key), keyDown: down) else { continue }
            event.flags = flags
            event.setIntegerValueField(.eventSourceUserData, value: Capture.syntheticInputTag)
            event.post(tap: .cghidEventTap)
        }
    }

    static var modifiersHeld: Bool {
        !CGEventSource.flagsState(.combinedSessionState).isDisjoint(with: [.maskCommand, .maskControl, .maskAlternate, .maskShift])
    }

    /// The hotkey's own modifiers would otherwise combine with the Cmd+C sent next.
    static func waitForModifiersReleased(timeout: TimeInterval) async {
        let deadline = Date().addingTimeInterval(timeout)
        while modifiersHeld && Date() < deadline { try? await Task.sleep(nanoseconds: 15_000_000) }
    }
}

struct PasteboardSnapshot {
    private let items: [[(NSPasteboard.PasteboardType, Data)]]

    static func take() -> PasteboardSnapshot {
        let items = (NSPasteboard.general.pasteboardItems ?? []).map { item in
            item.types.compactMap { type in item.data(forType: type).map { (type, $0) } }
        }
        return PasteboardSnapshot(items: items)
    }

    func restore() {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        guard !items.isEmpty else { return }
        pasteboard.writeObjects(items.map { entries in
            let item = NSPasteboardItem()
            for (type, data) in entries { item.setData(data, forType: type) }
            return item
        })
    }
}

enum Pasteboard {
    static var changeCount: Int { NSPasteboard.general.changeCount }

    static func readText() -> String? {
        let pasteboard = NSPasteboard.general
        if let text = pasteboard.string(forType: .string) { return text }
        for type in [NSPasteboard.PasteboardType.rtf, .rtfd] {
            if let data = pasteboard.data(forType: type),
               let text = NSAttributedString(rtf: data, documentAttributes: nil)?.string ?? NSAttributedString(rtfd: data, documentAttributes: nil)?.string {
                return text
            }
        }
        return nil
    }

    /// Writes the translation for pasting. It stays on this Mac (no Universal Clipboard) and is marked
    /// transient so clipboard managers that follow nspasteboard.org skip it.
    static func setPrivate(_ text: String) -> Int {
        let pasteboard = NSPasteboard.general
        pasteboard.prepareForNewContents(with: .currentHostOnly)
        pasteboard.setString(text, forType: .string)
        pasteboard.setData(Data(), forType: NSPasteboard.PasteboardType("org.nspasteboard.TransientType"))
        return pasteboard.changeCount
    }
}

enum KeyCode {
    static let a = kVK_ANSI_A, c = kVK_ANSI_C, v = kVK_ANSI_V, escape = kVK_Escape
}
