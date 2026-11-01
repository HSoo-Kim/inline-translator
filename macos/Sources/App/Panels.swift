import AppKit

/// Borderless panel that never takes focus from the app being translated.
class FloatingPanel: NSPanel {
    init(clickThrough: Bool) {
        super.init(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = true
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        ignoresMouseEvents = clickThrough
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    /// Places the panel by its top-left corner, in top-left screen coordinates.
    func place(at topLeft: CGPoint, size: CGSize) {
        setFrame(ScreenSpace.appKit(CGRect(origin: topLeft, size: size)), display: true)
    }
}

enum Palette {
    static let ink = NSColor(srgbRed: 31 / 255, green: 41 / 255, blue: 55 / 255, alpha: 1)
    static let muted = NSColor(srgbRed: 107 / 255, green: 114 / 255, blue: 128 / 255, alpha: 1)
    static let gray = NSColor(srgbRed: 156 / 255, green: 163 / 255, blue: 175 / 255, alpha: 1)
    static let line = NSColor(srgbRed: 226 / 255, green: 230 / 255, blue: 237 / 255, alpha: 1)
    static let accent = NSColor(srgbRed: 37 / 255, green: 99 / 255, blue: 235 / 255, alpha: 1)
    static let green = NSColor(srgbRed: 22 / 255, green: 163 / 255, blue: 74 / 255, alpha: 1)
    static let greenInk = NSColor(srgbRed: 21 / 255, green: 128 / 255, blue: 61 / 255, alpha: 1)
    static let red = NSColor(srgbRed: 220 / 255, green: 38 / 255, blue: 38 / 255, alpha: 1)
    static let amber = NSColor(srgbRed: 217 / 255, green: 119 / 255, blue: 6 / 255, alpha: 1)
}

private final class CardView: NSView {
    let stack = NSStackView()
    var onPress: (() -> Void)?

    init(background: NSColor, border: NSColor, padding: NSEdgeInsets) {
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 10
        layer?.backgroundColor = background.cgColor
        layer?.borderColor = border.cgColor
        layer?.borderWidth = 1
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 0
        stack.edgeInsets = padding
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor), stack.trailingAnchor.constraint(equalTo: trailingAnchor),
            stack.topAnchor.constraint(equalTo: topAnchor), stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    override func accessibilityPerformPress() -> Bool {
        guard let onPress else { return false }
        onPress()
        return true
    }
}

private func label(_ text: String, size: CGFloat, color: NSColor, width: CGFloat, weight: NSFont.Weight = .regular) -> NSTextField {
    let field = NSTextField(wrappingLabelWithString: text)
    field.font = .systemFont(ofSize: size, weight: weight)
    field.textColor = color
    field.preferredMaxLayoutWidth = width
    field.isSelectable = false
    return field
}

/// Read-only translation shown near the selection or the mouse. Closes on any key press or click.
final class TranslationBubble {
    private let panel = FloatingPanel(clickThrough: true)
    private var monitors: [Any] = []
    private var heldFlags: NSEvent.ModifierFlags = []
    private var followMouse = false
    private(set) var isVisible = false
    private(set) var text = ""

    static func content(title: String, body: String, hint: String, dark: Bool) -> NSView {
        let card = CardView(background: dark ? NSColor(srgbRed: 24 / 255, green: 24 / 255, blue: 27 / 255, alpha: 1) : .white,
                            border: dark ? NSColor(srgbRed: 55 / 255, green: 55 / 255, blue: 60 / 255, alpha: 1) : Palette.line,
                            padding: NSEdgeInsets(top: 12, left: 16, bottom: 12, right: 16))
        let header = label(title, size: 11, color: dark ? NSColor(white: 0.75, alpha: 1) : Palette.muted, width: 440)
        let content = label(body, size: 14, color: dark ? .white : NSColor(white: 0.07, alpha: 1), width: 440)
        let footer = label(hint, size: 10.5, color: dark ? NSColor(white: 0.57, alpha: 1) : Palette.gray, width: 440)
        card.stack.addArrangedSubview(header)
        card.stack.setCustomSpacing(4, after: header)
        card.stack.addArrangedSubview(content)
        card.stack.setCustomSpacing(8, after: content)
        card.stack.addArrangedSubview(footer)
        card.setAccessibilityElement(true)
        card.setAccessibilityRole(.staticText)
        card.setAccessibilityLabel(body)
        return card
    }

    func show(near target: CGRect, title: String, body: String, hint: String, dark: Bool, followMouse: Bool) {
        hide()
        text = body
        let card = TranslationBubble.content(title: title, body: body, hint: hint, dark: dark)
        panel.contentView = card
        let size = card.fittingSize
        panel.place(at: BubbleLayout.placement(target: target, size: size, area: ScreenSpace.visibleArea(near: target.origin), gap: 8),
                    size: size)
        self.followMouse = followMouse
        heldFlags = NSEvent.modifierFlags.intersection(.deviceIndependentFlagsMask)
        panel.orderFrontRegardless()
        isVisible = true
        let closing: NSEvent.EventTypeMask = [.keyDown, .flagsChanged, .leftMouseDown, .rightMouseDown, .otherMouseDown]
        let moving: NSEvent.EventTypeMask = followMouse ? [.mouseMoved, .leftMouseDragged] : []
        if let global = NSEvent.addGlobalMonitorForEvents(matching: closing.union(moving), handler: { [weak self] in self?.handle($0) }) {
            monitors.append(global)
        }
        if let local = NSEvent.addLocalMonitorForEvents(matching: closing.union(moving), handler: { [weak self] event in
            self?.handle(event)
            return event
        }) {
            monitors.append(local)
        }
    }

    func hide() {
        monitors.forEach(NSEvent.removeMonitor)
        monitors.removeAll()
        panel.orderOut(nil)
        isVisible = false
    }

    private func handle(_ event: NSEvent) {
        switch event.type {
        case .mouseMoved, .leftMouseDragged:
            let mouse = ScreenSpace.mouse
            let size = panel.frame.size
            panel.place(at: BubbleLayout.placement(target: CGRect(x: mouse.x + 12, y: mouse.y, width: 1, height: 12), size: size,
                                                   area: ScreenSpace.visibleArea(near: mouse), gap: 8), size: size)
        case .flagsChanged:
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            let pressed = !flags.subtracting(heldFlags).isEmpty
            heldFlags = flags
            if pressed { hide() }
        case .keyDown where event.isARepeat:
            break
        default:
            hide()
        }
    }
}

/// "Translating to English… · Esc to cancel" near the selection while the engine works.
final class BusyIndicator {
    private let panel = FloatingPanel(clickThrough: true)

    static func content(text: String) -> NSView {
        let card = CardView(background: .white, border: Palette.line, padding: NSEdgeInsets(top: 7, left: 10, bottom: 7, right: 12))
        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 8
        let spinner = NSProgressIndicator()
        spinner.style = .spinning
        spinner.controlSize = .small
        spinner.startAnimation(nil)
        row.addArrangedSubview(spinner)
        row.addArrangedSubview(label(text, size: 12, color: Palette.ink, width: 360))
        card.stack.addArrangedSubview(row)
        card.setAccessibilityElement(true)
        card.setAccessibilityRole(.staticText)
        card.setAccessibilityLabel(text)
        return card
    }

    func show(near target: CGRect, text: String) {
        let card = BusyIndicator.content(text: text)
        panel.contentView = card
        let size = card.fittingSize
        panel.place(at: BubbleLayout.placement(target: target, size: size, area: ScreenSpace.visibleArea(near: target.origin), gap: 6),
                    size: size)
        panel.orderFrontRegardless()
    }

    func hide() { panel.orderOut(nil) }
}

enum ToastKind { case info, warning, error }

/// Notification under the menu bar icon, the macOS stand-in for the Windows tray balloon.
/// Clicking runs the attached action, if any; it also disappears by itself.
final class Toast {
    private let panel = FloatingPanel(clickThrough: false)
    private var action: (() -> Void)?
    private var hideWork: DispatchWorkItem?
    private(set) var message = ""
    var anchor: () -> CGRect? = { nil }

    static func content(_ message: String, kind: ToastKind) -> NSView {
        let card = CardView(background: .white, border: Palette.line, padding: NSEdgeInsets(top: 10, left: 12, bottom: 10, right: 14))
        let row = NSStackView()
        row.orientation = .horizontal
        row.alignment = .top
        row.spacing = 10
        let dot = NSView()
        dot.wantsLayer = true
        dot.layer?.cornerRadius = 4
        dot.layer?.backgroundColor = (kind == .error ? Palette.red : kind == .warning ? Palette.amber : Palette.accent).cgColor
        dot.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([dot.widthAnchor.constraint(equalToConstant: 8), dot.heightAnchor.constraint(equalToConstant: 8)])
        let texts = NSStackView()
        texts.orientation = .vertical
        texts.alignment = .leading
        texts.spacing = 2
        texts.addArrangedSubview(label("Inline Translator", size: 12, color: Palette.ink, width: 300, weight: .semibold))
        texts.addArrangedSubview(label(message, size: 12, color: Palette.muted, width: 300))
        let dotHolder = NSStackView(views: [dot])
        dotHolder.edgeInsets = NSEdgeInsets(top: 4, left: 0, bottom: 0, right: 0)
        row.addArrangedSubview(dotHolder)
        row.addArrangedSubview(texts)
        card.stack.addArrangedSubview(row)
        card.setAccessibilityElement(true)
        card.setAccessibilityRole(.button)
        card.setAccessibilityLabel(message)
        return card
    }

    func show(_ message: String, kind: ToastKind, duration: TimeInterval = 5, action: (() -> Void)? = nil) {
        self.message = message
        self.action = action
        let card = Toast.content(message, kind: kind)
        card.addGestureRecognizer(NSClickGestureRecognizer(target: self, action: #selector(clicked)))
        (card as? CardView)?.onPress = { [weak self] in self?.clicked() }
        panel.contentView = card
        let size = card.fittingSize
        let anchorRect = anchor().map(ScreenSpace.topLeft) ?? CGRect(origin: ScreenSpace.mouse, size: .zero)
        let area = ScreenSpace.visibleArea(near: CGPoint(x: anchorRect.midX, y: anchorRect.maxY + 30))
        let x = min(max(anchorRect.midX - size.width / 2, area.minX + 8), area.maxX - size.width - 8)
        panel.place(at: CGPoint(x: x, y: area.minY + 8), size: size)
        panel.orderFrontRegardless()
        hideWork?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.hide() }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + duration, execute: work)
    }

    func hide() {
        hideWork?.cancel()
        panel.orderOut(nil)
        action = nil
    }

    @objc private func clicked() {
        let run = action
        hide()
        run?()
    }
}
