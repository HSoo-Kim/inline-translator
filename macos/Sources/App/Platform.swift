import AppKit
import ServiceManagement

enum AppDiscovery {
    static func installed() -> [AppEntry] {
        let manager = FileManager.default
        let roots = ["/Applications", "/Applications/Utilities", "/System/Applications", "/System/Applications/Utilities",
                     NSHomeDirectory() + "/Applications"]
        var bundles: [URL] = []
        for root in roots {
            for item in (try? manager.contentsOfDirectory(atPath: root)) ?? [] {
                let url = URL(fileURLWithPath: root).appendingPathComponent(item)
                if url.pathExtension == "app" {
                    bundles.append(url)
                } else if root == "/Applications", (try? url.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory == true {
                    // Vendors often install into a folder, for example /Applications/Microsoft Teams/…
                    for inner in (try? manager.contentsOfDirectory(atPath: url.path)) ?? [] where inner.hasSuffix(".app") {
                        bundles.append(url.appendingPathComponent(inner))
                    }
                }
            }
        }
        return bundles.compactMap { url in
            guard let id = Bundle(url: url)?.bundleIdentifier else { return nil }
            return AppEntry(bundleID: id, name: displayName(url), path: url.path, running: false)
        }
    }

    static func running() -> [AppEntry] {
        NSWorkspace.shared.runningApplications.compactMap { app in
            guard app.activationPolicy == .regular, let id = app.bundleIdentifier else { return nil }
            return AppEntry(bundleID: id, name: app.localizedName ?? id, path: app.bundleURL?.path, running: true)
        }
    }

    static func discover() -> [AppEntry] {
        let own = Set([Bundle.main.bundleIdentifier?.lowercased()].compactMap { $0 })
        return AppCatalog.merge(installed: installed(), running: running(), excluding: own)
    }

    static func name(for bundleID: String) -> String {
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) { return displayName(url) }
        return AppCatalog.knownNames[bundleID.lowercased()] ?? bundleID
    }

    static func icon(for bundleID: String) -> NSImage? {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) else { return nil }
        return NSWorkspace.shared.icon(forFile: url.path)
    }

    private static func displayName(_ url: URL) -> String {
        let name = FileManager.default.displayName(atPath: url.path)
        return name.hasSuffix(".app") ? String(name.dropLast(4)) : name
    }
}

enum LoginItem {
    static var isAvailable: Bool { Bundle.main.bundleURL.pathExtension == "app" }
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func set(_ enabled: Bool) throws {
        if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
    }
}

enum Relauncher {
    /// Opens the bundle again once this process has exited.
    static func relaunch(_ bundle: URL) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = ["-c", "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; /usr/bin/open \"$2\"",
                             "relaunch", String(ProcessInfo.processInfo.processIdentifier), bundle.path]
        try? process.run()
    }
}

enum SignInTerminal {
    /// Opens Terminal with the CLI's interactive sign-in.
    static func open(_ provider: Provider, environment: EngineEnvironment) throws {
        guard let executable = environment.resolve(provider) else {
            throw EngineError(message: "\(EngineCli.productName(provider)) CLI was not found. Install it and sign in once.")
        }
        let script = FileManager.default.temporaryDirectory
            .appendingPathComponent("InlineTranslator-sign-in-\(provider.rawValue).command")
        try EngineCli.signInScript(executable: executable, provider: provider, path: environment.variables["PATH"] ?? "")
            .write(to: script, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: script.path)
        guard NSWorkspace.shared.open(script) else { throw EngineError(message: "Could not open Terminal to sign in.") }
    }
}
