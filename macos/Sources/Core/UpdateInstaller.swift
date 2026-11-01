import CryptoKit
import Foundation

struct UpdateFailure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Replaces the running app bundle with the one inside a verified release disk image. macOS lets a running
/// bundle be moved, so the current copy moves aside, the new one takes its place, and the old one is deleted.
enum UpdateInstaller {
    static let appName = "Inline Translator.app"

    /// The running bundle, when it is an .app whose folder this user can write to.
    static func installableBundle(_ bundle: URL = Bundle.main.bundleURL) -> URL? {
        guard bundle.pathExtension == "app" else { return nil }
        return FileManager.default.isWritableFile(atPath: bundle.deletingLastPathComponent().path) ? bundle : nil
    }

    static func download(_ url: URL) async throws -> URL {
        var request = URLRequest(url: url, timeoutInterval: 300)
        request.setValue("InlineTranslator-mac/\(UpdateChecker.current)", forHTTPHeaderField: "User-Agent")
        let (file, response) = try await URLSession.shared.download(for: request)
        if let http = response as? HTTPURLResponse, http.statusCode != 200 {
            throw UpdateFailure(message: "The download failed with HTTP \(http.statusCode).")
        }
        return file
    }

    static func sha256(of file: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: file)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: 1 << 20), !chunk.isEmpty { hasher.update(data: chunk) }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    static func install(_ release: ReleaseInfo, into bundle: URL, bundleID: String,
                        download: (URL) async throws -> URL = UpdateInstaller.download) async throws {
        guard let packageURL = release.packageURL, let expected = release.packageSHA256 else {
            throw UpdateFailure(message: "The release has no verifiable macOS package.")
        }
        let manager = FileManager.default
        let image = try await download(packageURL)
        defer { try? manager.removeItem(at: image) }
        guard try sha256(of: image) == expected.lowercased() else {
            throw UpdateFailure(message: "The downloaded package failed its SHA-256 check.")
        }

        let mount = manager.temporaryDirectory.appendingPathComponent("InlineTranslator-update-" + UUID().uuidString)
        try manager.createDirectory(at: mount, withIntermediateDirectories: true)
        let staged = bundle.deletingLastPathComponent().appendingPathComponent(".\(appName).new")
        try? manager.removeItem(at: staged)
        defer { try? manager.removeItem(at: staged) }
        try await run("/usr/bin/hdiutil", ["attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount.path, image.path])
        do {
            let candidate = mount.appendingPathComponent(appName)
            guard Bundle(url: candidate)?.bundleIdentifier == bundleID else {
                throw UpdateFailure(message: "The package does not contain \(appName).")
            }
            try await run("/usr/bin/codesign", ["--verify", "--deep", "--strict", candidate.path])
            try await run("/usr/bin/ditto", [candidate.path, staged.path])
        } catch {
            _ = try? await run("/usr/bin/hdiutil", ["detach", mount.path, "-force"])
            try? manager.removeItem(at: mount)
            throw error
        }
        _ = try? await run("/usr/bin/hdiutil", ["detach", mount.path, "-force"])
        try? manager.removeItem(at: mount)
        try replace(bundle, with: staged)
    }

    static func replace(_ bundle: URL, with staged: URL) throws {
        let manager = FileManager.default
        let previous = bundle.deletingLastPathComponent().appendingPathComponent(".\(appName).old")
        try? manager.removeItem(at: previous)
        try manager.moveItem(at: bundle, to: previous)
        do {
            try manager.moveItem(at: staged, to: bundle)
        } catch {
            try? manager.moveItem(at: previous, to: bundle)
            throw error
        }
        try? manager.removeItem(at: previous)
    }

    private static func run(_ tool: String, _ arguments: [String]) async throws {
        let result = try await ProcessRunner.run(tool, arguments, input: "", directory: NSTemporaryDirectory(),
                                                 environment: ProcessInfo.processInfo.environment, timeout: 120)
        guard result.exitCode == 0 else {
            let detail = (result.error + result.output).trimmingCharacters(in: .whitespacesAndNewlines)
            throw UpdateFailure(message: "\(URL(fileURLWithPath: tool).lastPathComponent) failed: \(detail)")
        }
    }
}

enum ShellPath {
    /// PATH as the user's login shell sets it, so CLIs installed through nvm, Homebrew and the like are found.
    static func load(shell: String = ProcessInfo.processInfo.environment["SHELL"] ?? "/bin/zsh") async -> String? {
        guard FileManager.default.isExecutableFile(atPath: shell) else { return nil }
        let value = shell.hasSuffix("/fish") ? "(string join : $PATH)" : "\"$PATH\""
        let script = "printf '\\n__IT_PATH__%s__IT_END__' \(value)"
        guard let result = try? await ProcessRunner.run(shell, ["-ilc", script], input: "", directory: NSHomeDirectory(),
                                                        environment: ProcessInfo.processInfo.environment, timeout: 5) else { return nil }
        return parse(result.output)
    }

    static func parse(_ output: String) -> String? {
        guard let start = output.range(of: "__IT_PATH__", options: .backwards),
              let end = output.range(of: "__IT_END__", range: start.upperBound..<output.endIndex) else { return nil }
        let path = String(output[start.upperBound..<end.lowerBound]).trimmingCharacters(in: .whitespacesAndNewlines)
        return path.isEmpty ? nil : path
    }
}
