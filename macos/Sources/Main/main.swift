import AppKit

var settingsURL = SettingsStore.defaultURL
let arguments = CommandLine.arguments
let customSettings = arguments.firstIndex(of: "--settings").flatMap { $0 + 1 < arguments.count ? arguments[$0 + 1] : nil }
if let customSettings { settingsURL = URL(fileURLWithPath: customSettings).standardizedFileURL }

// Another copy (for example one opened from the disk image while /Applications already runs) would translate and
// paste a second time. Hand over to it instead: reopening it shows its Settings, like launching the app again on Windows.
if customSettings == nil, let id = Bundle.main.bundleIdentifier,
   let running = NSRunningApplication.runningApplications(withBundleIdentifier: id)
    .first(where: { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }), let url = running.bundleURL {
    let opened = DispatchSemaphore(value: 0)
    NSWorkspace.shared.openApplication(at: url, configuration: NSWorkspace.OpenConfiguration()) { _, _ in opened.signal() }
    _ = opened.wait(timeout: .now() + 5)
    exit(0)
}

let loaded = SettingsStore.load(settingsURL)
let firstRun = loaded == nil
let settings = loaded ?? AppSettings().normalized()
if firstRun {
    do { try SettingsStore.save(settingsURL, settings) } catch { ErrorLog.write("Could not create the settings file", error) }
}

let application = NSApplication.shared
let controller = MainActor.assumeIsolated { AppController(settingsURL: settingsURL, settings: settings, firstRun: firstRun) }
application.delegate = controller
application.setActivationPolicy(.accessory)
application.run()
