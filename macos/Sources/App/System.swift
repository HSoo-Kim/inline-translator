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

    static func element(_ value: AnyObject?) -> AXUIElement? {
        guard let value, CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return (value as! AXUIElement)
    }

    static func focusedElement(_ pid: pid_t) -> AXUIElement? {
        let focused = element(attribute(application(pid), kAXFocusedUIElementAttribute))
        if let focused { AXUIElementSetMessagingTimeout(focused, 0.5) }
        return focused
    }

    static func focusedWindow(_ pid: pid_t) -> AXUIElement? {
        element(attribute(application(pid), kAXFocusedWindowAttribute))
    }

    /// Read-only only when the app says so: static text or a web page whose selection cannot be edited, or a PDF
    /// document (pages of static text). Pasting into a PDF in Preview would at best do nothing and at worst add an
    /// annotation, so PDFs always get the bubble. Anything unknown counts as editable (Mail compose is a web area).
    static func isReadOnly(_ element: AXUIElement?) -> Bool {
        guard let element, let role = attribute(element, kAXRoleAttribute) as? String else { return false }
        if isPDFDocument(element, role: role) { return true }
        guard role == kAXStaticTextRole || role == "AXWebArea" else { return false }
        return !settable(element, kAXSelectedTextAttribute) && !settable(element, kAXValueAttribute)
    }

    static func isPDFDocument(_ element: AXUIElement, role: String) -> Bool {
        guard role == kAXGroupRole || role == "AXPage" else { return false }
        if role == "AXPage" { return true }
        let pages = attribute(element, "AXVisibleChildren") as? [AXUIElement] ?? attribute(element, kAXChildrenAttribute) as? [AXUIElement]
        return pages?.first.map { attribute($0, kAXRoleAttribute) as? String == "AXPage" } ?? false
    }

    private static func settable(_ element: AXUIElement, _ name: String) -> Bool {
        var result = DarwinBoolean(false)
        return AXUIElementIsAttributeSettable(element, name as CFString, &result) == .success && result.boolValue
    }

    /// Screen rectangle of the selected text (top-left origin), when the app exposes it.
    static func selectionBounds(_ element: AXUIElement?) -> CGRect? {
        guard let element, let range = attribute(element, kAXSelectedTextRangeAttribute) else { return nil }
        var bounds: AnyObject?
        guard AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute as CFString,
                                                         range, &bounds) == .success,
              let bounds, CFGetTypeID(bounds) == AXValueGetTypeID() else { return nil }
        var rect = CGRect.zero
        guard AXValueGetValue(bounds as! AXValue, .cgRect, &rect), rect.width > 0, rect.height > 0 else { return nil }
        return rect
    }

    static func parameter(_ element: AXUIElement, _ name: String, _ argument: AnyObject) -> AnyObject? {
        var value: AnyObject?
        return AXUIElementCopyParameterizedAttributeValue(element, name as CFString, argument, &value) == .success ? value : nil
    }

    static func selectedRange(_ element: AXUIElement) -> NSRange? {
        guard let value = attribute(element, kAXSelectedTextRangeAttribute), CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
        var range = CFRange()
        guard AXValueGetValue(value as! AXValue, .cfRange, &range), range.location >= 0, range.length > 0 else { return nil }
        return NSRange(location: range.location, length: range.length)
    }

    private static func string(_ element: AXUIElement, _ range: NSRange) -> String? {
        var span = CFRange(location: range.location, length: range.length)
        guard let argument = AXValueCreate(.cfRange, &span) else { return nil }
        return parameter(element, kAXStringForRangeParameterizedAttribute, argument) as? String
    }

    /// The text around the selection, read without touching the selection or the pasteboard.
    /// Text fields and editors report character ranges; web pages (Safari, Chrome, Mail) report text markers.
    /// nil when the app exposes neither or reports text that does not contain what was copied.
    static func selectionContext(_ element: AXUIElement?, selected: String) -> TranslationContext? {
        guard let element else { return nil }
        // Each source must contain the copied text; otherwise try the next one (web areas also report ranges).
        if let selection = selectedRange(element) {
            if let total = attribute(element, kAXNumberOfCharactersAttribute) as? Int, NSMaxRange(selection) <= total {
                let start = max(0, selection.location - ContextWindow.maxBefore - 1)
                let end = min(total, NSMaxRange(selection) + ContextWindow.maxAfter + 1)
                if let chunk = string(element, NSRange(location: start, length: end - start)),
                   let context = ContextWindow.around(text: chunk, selection: NSRange(location: selection.location - start,
                                                                                      length: selection.length), selected: selected) {
                    return context
                }
            }
            if let value = attribute(element, kAXValueAttribute) as? String, (value as NSString).length <= 500_000,
               let context = ContextWindow.around(text: value, selection: selection, selected: selected) {
                return context
            }
        }
        if attribute(element, kAXRoleAttribute) as? String == "AXWebArea" { return webContext(element, selected: selected) }
        // Follow only the selection's ancestors. Searching the whole window could attach PDF text to a
        // selection in an unrelated control, such as Preview's Find field.
        var node: AXUIElement? = element
        let deadline = Date().addingTimeInterval(0.5)
        for _ in 0..<10 {
            guard let current = node, Date() < deadline, !Task.isCancelled else { break }
            let role = attribute(current, kAXRoleAttribute) as? String ?? ""
            if isPDFDocument(current, role: role) { return pageContext(current, selected: selected) }
            if role == kAXWindowRole { break }
            node = self.element(attribute(current, kAXParentAttribute))
        }
        return nil
    }

    /// PDF viewers such as Preview report only the selected text on the focused document, and list each page as
    /// one static text per line. Read the lines of the pages on screen and find the selection among them.
    private static func pageContext(_ element: AXUIElement, selected: String) -> TranslationContext? {
        var lines: [String] = [], length = 0, budget = 4000
        let textLimit = 200_000, deadline = Date().addingTimeInterval(0.5)
        var hint: Int?
        let names = [kAXRoleAttribute, kAXValueAttribute, kAXSelectedTextRangeAttribute, kAXChildrenAttribute] as CFArray
        func visit(_ node: AXUIElement, _ depth: Int) {
            guard depth < 14, budget > 0, length < textLimit, Date() < deadline, !Task.isCancelled else { return }
            budget -= 1
            var values: CFArray?
            guard AXUIElementCopyMultipleAttributeValues(node, names, [], &values) == .success,
                  let list = values as? [AnyObject], list.count == 4 else { return }
            if list[0] as? String == kAXStaticTextRole, let text = list[1] as? String {
                let count = (text as NSString).length
                guard count < textLimit - length else { budget = 0; return }
                var range = CFRange()
                if hint == nil, CFGetTypeID(list[2]) == AXValueGetTypeID(), AXValueGetValue(list[2] as! AXValue, .cfRange, &range),
                   range.location >= 0, range.location != NSNotFound, range.length > 0 {
                    hint = length + range.location
                }
                lines.append(text)
                length += count + 1
                return
            }
            // Only the pages on screen: a long document has many more, and the selection is on one of these.
            let visible = depth == 0 ? attribute(node, "AXVisibleChildren") as? [AXUIElement] : nil
            for child in visible ?? list[3] as? [AXUIElement] ?? [] {
                guard budget > 0, length < textLimit, Date() < deadline, !Task.isCancelled else { break }
                visit(child, depth + 1)
            }
        }
        visit(element, 0)
        guard !lines.isEmpty else { return nil }
        return ContextWindow.around(page: lines.joined(separator: "\n"), selected: selected, near: hint)
    }

    private static func webContext(_ element: AXUIElement, selected: String) -> TranslationContext? {
        guard let range = attribute(element, "AXSelectedTextMarkerRange"), CFGetTypeID(range) == AXTextMarkerRangeGetTypeID(),
              let current = parameter(element, "AXStringForTextMarkerRange", range) as? String,
              ContextWindow.same(current, selected) else { return nil }
        let start: AnyObject = AXTextMarkerRangeCopyStartMarker(range as! AXTextMarkerRange)
        let end: AnyObject = AXTextMarkerRangeCopyEndMarker(range as! AXTextMarkerRange)
        func text(from first: AnyObject, to second: AnyObject) -> String {
            guard let range = parameter(element, "AXTextMarkerRangeForUnorderedTextMarkers", [first, second] as CFArray) else { return "" }
            return parameter(element, "AXStringForTextMarkerRange", range) as? String ?? ""
        }
        // Widens one paragraph at a time. Past the first or last paragraph WebKit returns an empty marker, whose range
        // has no text, so stop at the last step that still added text.
        func widen(from anchor: AnyObject, _ name: String, steps: Int, before: Bool) -> String {
            var marker = anchor, best = ""
            for _ in 0..<steps {
                guard let next = parameter(element, name, marker) else { break }
                let candidate = before ? text(from: next, to: anchor) : text(from: anchor, to: next)
                if candidate.isEmpty || candidate == best { break }
                best = candidate
                marker = next
            }
            return best
        }
        // The current paragraph plus two before it, and the current paragraph plus one after it; clip() bounds the size.
        return ContextWindow.clip(before: widen(from: start, "AXPreviousParagraphStartTextMarkerForTextMarker", steps: 3, before: true),
                                  after: widen(from: end, "AXNextParagraphEndTextMarkerForTextMarker", steps: 2, before: false))
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
            // Some apps check the device-dependent left-Command bit as well as the generic Command flag.
            event.flags = flags.contains(.maskCommand) ? flags.union(CGEventFlags(rawValue: 0x8)) : flags
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
    static let escape = kVK_Escape
    static var a: Int { command("a", fallback: kVK_ANSI_A) }
    static var c: Int { command("c", fallback: kVK_ANSI_C) }
    static var v: Int { command("v", fallback: kVK_ANSI_V) }

    /// The key that types `character` while Command is held in the current keyboard layout. Key codes name physical
    /// positions, so the US position of A would send Cmd+Q on AZERTY and Cmd+C would become Cmd+J on Dvorak.
    /// With Command held, layouts such as "Dvorak – QWERTY ⌘" report their QWERTY shortcut keys.
    static func command(_ character: Character, fallback: Int,
                        layout source: TISInputSource? = TISCopyCurrentASCIICapableKeyboardLayoutInputSource()?.takeRetainedValue()) -> Int {
        guard let source, let pointer = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData),
              let bytes = CFDataGetBytePtr(Unmanaged<CFData>.fromOpaque(pointer).takeUnretainedValue()) else { return fallback }
        return bytes.withMemoryRebound(to: UCKeyboardLayout.self, capacity: 1) { layout in
            for code in [fallback] + Array(0..<128) {
                var deadKeys: UInt32 = 0
                var length = 0
                var characters = [UniChar](repeating: 0, count: 4)
                let status = UCKeyTranslate(layout, UInt16(code), UInt16(kUCKeyActionDown), UInt32((cmdKey >> 8) & 0xFF),
                                            UInt32(LMGetKbdType()), OptionBits(1 << kUCKeyTranslateNoDeadKeysBit),
                                            &deadKeys, characters.count, &length, &characters)
                if status == noErr, length == 1, String(utf16CodeUnits: characters, count: 1).lowercased() == String(character) {
                    return code
                }
            }
            return fallback
        }
    }
}
