import AppKit

var settingsURL = SettingsStore.defaultURL
let arguments = CommandLine.arguments
if let index = arguments.firstIndex(of: "--settings"), index + 1 < arguments.count {
    settingsURL = URL(fileURLWithPath: arguments[index + 1]).standardizedFileURL
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
