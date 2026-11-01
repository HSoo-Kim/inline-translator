import Foundation

enum ErrorLog {
    private static let maxBytes = 256 * 1024
    private static let lock = NSLock()

    nonisolated(unsafe) static var fileURL = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("Logs/InlineTranslator/error.log")

    // Only our own context and the error message are written: never the text being translated.
    static func write(_ context: String, _ error: Error) {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
        formatter.locale = Locale(identifier: "en_US_POSIX")
        let line = "\(formatter.string(from: Date())) \(context): \(type(of: error)): \(error.localizedDescription)\n"
        lock.lock()
        defer { lock.unlock() }
        let manager = FileManager.default
        do {
            try manager.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            if let size = (try? manager.attributesOfItem(atPath: fileURL.path))?[.size] as? Int, size > maxBytes {
                try manager.removeItem(at: fileURL)
            }
            if let handle = try? FileHandle(forWritingTo: fileURL) {
                defer { try? handle.close() }
                try handle.seekToEnd()
                try handle.write(contentsOf: Data(line.utf8))
            } else {
                try Data(line.utf8).write(to: fileURL)
            }
        } catch {
            NSLog("Error log unavailable: %@ / %@", error.localizedDescription, line)
        }
    }
}
