import Foundation

struct AppVersion: Comparable, Hashable, CustomStringConvertible {
    let major: Int, minor: Int, patch: Int

    init(_ major: Int, _ minor: Int, _ patch: Int) {
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    init?(_ text: String) {
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard (2...3).contains(parts.count) else { return nil }
        let numbers = parts.map { part in part.allSatisfy(\.isASCII) ? Int(part) : nil }
        guard numbers.allSatisfy({ $0 != nil && $0! >= 0 }) else { return nil }
        self.init(numbers[0]!, numbers[1]!, numbers.count > 2 ? numbers[2]! : 0)
    }

    var description: String { "\(major).\(minor).\(patch)" }

    static func < (lhs: AppVersion, rhs: AppVersion) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }
}

struct ReleaseInfo: Equatable {
    let version: AppVersion
    let tag: String
    let url: URL
    let packageURL: URL?
    let packageSHA256: String?
}

/// macOS releases share the repository with the Windows app. They use "mac-v" tags and are never marked
/// latest, so the Windows updater (which reads releases/latest) keeps seeing only Windows releases.
enum UpdateChecker {
    static let releasesAPI = URL(string: "https://api.github.com/repos/HSoo-Kim/inline-translator/releases?per_page=30")!
    static let tagPrefix = "mac-v"
    static let packageName = "inline-translator-mac.dmg"
    static let checkInterval: TimeInterval = 5 * 60

    static var current: AppVersion {
        AppVersion(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "") ?? AppVersion(0, 0, 0)
    }

    // Reads only public release metadata; nothing about the user or their text is sent.
    static func fetchLatest() async throws -> ReleaseInfo? {
        var request = URLRequest(url: releasesAPI, timeoutInterval: 10)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("InlineTranslator-mac/\(current)", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        if let http = response as? HTTPURLResponse, http.statusCode != 200 {
            throw URLError(.badServerResponse, userInfo: [NSLocalizedDescriptionKey: "GitHub answered \(http.statusCode)."])
        }
        return try latest(in: data)
    }

    static func latest(in data: Data) throws -> ReleaseInfo? {
        guard let releases = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
            throw URLError(.cannotParseResponse)
        }
        return releases.compactMap(parse).max { $0.version < $1.version }
    }

    static func parse(_ release: [String: Any]) -> ReleaseInfo? {
        guard release["draft"] as? Bool != true, release["prerelease"] as? Bool != true,
              let tag = release["tag_name"] as? String, tag.hasPrefix(tagPrefix),
              let version = AppVersion(String(tag.dropFirst(tagPrefix.count))),
              let page = release["html_url"] as? String, page.hasPrefix("https://github.com/"),
              let url = URL(string: page) else { return nil }
        var packageURL: URL?, sha256: String?
        for asset in release["assets"] as? [[String: Any]] ?? [] where asset["name"] as? String == packageName {
            let download = asset["browser_download_url"] as? String ?? ""
            let digest = asset["digest"] as? String ?? ""
            let hex = digest.dropFirst("sha256:".count)
            if download.hasPrefix("https://github.com/"), digest.hasPrefix("sha256:"), hex.count == 64,
               hex.allSatisfy(\.isHexDigit) {
                packageURL = URL(string: download)
                sha256 = String(hex).lowercased()
            }
            break
        }
        return ReleaseInfo(version: version, tag: tag, url: url, packageURL: packageURL, packageSHA256: sha256)
    }
}
