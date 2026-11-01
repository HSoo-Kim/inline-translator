import Carbon.HIToolbox
import Foundation

/// System-wide shortcuts through Carbon `RegisterEventHotKey`, which needs no special permission.
final class HotkeyCenter {
    private static let signature: OSType = 0x4954_5248 // "ITRH"
    private var registered: [UInt32: EventHotKeyRef] = [:]
    private var handler: EventHandlerRef?
    var onPress: ((UInt32) -> Void)?

    init() {
        var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, context in
            guard let event, let context else { return OSStatus(eventNotHandledErr) }
            var id = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                           nil, MemoryLayout<EventHotKeyID>.size, nil, &id)
            guard status == noErr, id.signature == HotkeyCenter.signature else { return OSStatus(eventNotHandledErr) }
            let center = Unmanaged<HotkeyCenter>.fromOpaque(context).takeUnretainedValue()
            DispatchQueue.main.async { center.onPress?(id.id) }
            return noErr
        }, 1, &type, Unmanaged.passUnretained(self).toOpaque(), &handler)
    }

    /// False when another app already owns the shortcut.
    func register(_ id: UInt32, _ hotkey: Hotkey) -> Bool {
        var reference: EventHotKeyRef?
        let status = RegisterEventHotKey(hotkey.keyCode, hotkey.modifiers.carbonFlags,
                                         EventHotKeyID(signature: HotkeyCenter.signature, id: id),
                                         GetApplicationEventTarget(), OptionBits(kEventHotKeyExclusive), &reference)
        guard status == noErr, let reference else { return false }
        registered[id] = reference
        return true
    }

    func unregisterAll() {
        registered.values.forEach { UnregisterEventHotKey($0) }
        registered.removeAll()
    }

    deinit {
        unregisterAll()
        if let handler { RemoveEventHandler(handler) }
    }
}
