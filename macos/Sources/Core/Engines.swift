import Darwin
import Foundation

struct ProcessResult: Equatable {
    let exitCode: Int32
    let output: String
    let error: String
}

struct EngineError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

struct EngineTimeout: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

enum ProcessRunner {
    /// Runs a CLI in its own process group, so timeout and cancellation stop everything it started.
    static func run(_ executable: String, _ arguments: [String], input: String, directory: String,
                    environment: [String: String], timeout: TimeInterval) async throws -> ProcessResult {
        try Task.checkCancellation()
        let child = try SpawnedProcess(executable, arguments, directory: directory, environment: environment)
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                child.start(input: Data(input.utf8), timeout: timeout) { result in continuation.resume(with: result) }
            }
        } onCancel: {
            child.stop(cancelled: true)
        }
    }
}

private final class SpawnedProcess: @unchecked Sendable {
    private let pid: pid_t
    private let name: String
    private let stdin: Int32, stdout: Int32, stderr: Int32
    private let lock = NSLock()
    private var stopReason: Int = 0 // 1 timeout, 2 cancelled
    private var finished = false

    init(_ executable: String, _ arguments: [String], directory: String, environment: [String: String]) throws {
        name = URL(fileURLWithPath: executable).lastPathComponent
        var inPipe: [Int32] = [0, 0], outPipe: [Int32] = [0, 0], errPipe: [Int32] = [0, 0]
        guard pipe(&inPipe) == 0, pipe(&outPipe) == 0, pipe(&errPipe) == 0 else {
            throw EngineError(message: "Could not create pipes for \(name).")
        }
        var actions: posix_spawn_file_actions_t?
        posix_spawn_file_actions_init(&actions)
        defer { posix_spawn_file_actions_destroy(&actions) }
        posix_spawn_file_actions_adddup2(&actions, inPipe[0], 0)
        posix_spawn_file_actions_adddup2(&actions, outPipe[1], 1)
        posix_spawn_file_actions_adddup2(&actions, errPipe[1], 2)
        posix_spawn_file_actions_addchdir_np(&actions, directory)

        var attributes: posix_spawnattr_t?
        posix_spawnattr_init(&attributes)
        defer { posix_spawnattr_destroy(&attributes) }
        // New process group (for group kill), only the three pipes inherited, default signal handling.
        posix_spawnattr_setflags(&attributes, Int16(POSIX_SPAWN_SETPGROUP | POSIX_SPAWN_CLOEXEC_DEFAULT | POSIX_SPAWN_SETSIGDEF | POSIX_SPAWN_SETSIGMASK))
        posix_spawnattr_setpgroup(&attributes, 0)
        var defaults = sigset_t(), empty = sigset_t()
        sigemptyset(&defaults)
        sigaddset(&defaults, SIGPIPE)
        sigemptyset(&empty)
        posix_spawnattr_setsigdefault(&attributes, &defaults)
        posix_spawnattr_setsigmask(&attributes, &empty)

        let argv = ([executable] + arguments).map { strdup($0) } + [nil]
        let envp = environment.map { strdup("\($0.key)=\($0.value)") } + [nil]
        defer {
            argv.forEach { free($0) }
            envp.forEach { free($0) }
        }
        var spawned: pid_t = 0
        let status = posix_spawn(&spawned, executable, &actions, &attributes, argv, envp)
        close(inPipe[0])
        close(outPipe[1])
        close(errPipe[1])
        guard status == 0 else {
            close(inPipe[1])
            close(outPipe[0])
            close(errPipe[0])
            throw EngineError(message: "Could not start \(name): \(String(cString: strerror(status)))")
        }
        pid = spawned
        stdin = inPipe[1]
        stdout = outPipe[0]
        stderr = errPipe[0]
        // Writing to a CLI that already exited must fail with EPIPE, not kill this app with SIGPIPE.
        _ = fcntl(stdin, F_SETNOSIGPIPE, 1)
    }

    func start(input: Data, timeout: TimeInterval, completion: @escaping @Sendable (Result<ProcessResult, Error>) -> Void) {
        let group = DispatchGroup()
        var output = Data(), error = Data()
        let queue = DispatchQueue.global(qos: .userInitiated)
        queue.async {
            // The CLI may exit before reading its input (for example a bad flag); its exit code and stderr explain why.
            input.withUnsafeBytes { buffer in
                var offset = 0
                while offset < buffer.count {
                    let written = write(self.stdin, buffer.baseAddress! + offset, buffer.count - offset)
                    if written <= 0 { break }
                    offset += written
                }
            }
            close(self.stdin)
        }
        group.enter()
        queue.async { output = SpawnedProcess.readAll(self.stdout); group.leave() }
        group.enter()
        queue.async { error = SpawnedProcess.readAll(self.stderr); group.leave() }
        queue.asyncAfter(deadline: .now() + timeout) { [weak self] in self?.stop(cancelled: false) }
        queue.async {
            var status: Int32 = 0
            while waitpid(self.pid, &status, 0) == -1 && errno == EINTR {}
            // Anything the CLI left running in its group must not keep the pipes open.
            kill(-self.pid, SIGKILL)
            self.lock.lock()
            self.finished = true
            self.lock.unlock()
            group.wait()
            let exitCode: Int32 = (status & 0x7F) == 0 ? (status >> 8) & 0xFF : 128 + (status & 0x7F)
            self.lock.lock()
            let reason = self.stopReason
            self.lock.unlock()
            switch reason {
            case 2: completion(.failure(CancellationError()))
            case 1: completion(.failure(EngineTimeout(message: "\(self.name) did not answer within \(Int(timeout)) seconds.")))
            default: completion(.success(ProcessResult(exitCode: exitCode,
                                                       output: String(decoding: output, as: UTF8.self),
                                                       error: String(decoding: error, as: UTF8.self))))
            }
        }
    }

    func stop(cancelled: Bool) {
        lock.lock()
        defer { lock.unlock() }
        guard !finished else { return }
        if stopReason == 0 { stopReason = cancelled ? 2 : 1 }
        kill(-pid, SIGTERM)
        DispatchQueue.global().asyncAfter(deadline: .now() + 1) { [weak self] in
            guard let self else { return }
            self.lock.lock()
            if !self.finished { kill(-self.pid, SIGKILL) }
            self.lock.unlock()
        }
    }

    private static func readAll(_ descriptor: Int32) -> Data {
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 65536)
        while true {
            let count = read(descriptor, &buffer, buffer.count)
            if count > 0 { data.append(buffer, count: count) }
            else if count < 0 && errno == EINTR { continue }
            else { break }
        }
        close(descriptor)
        return data
    }
}

enum SignIn: Equatable { case notInstalled, signedIn, signedOut, unknown }

struct ModelOption: Hashable {
    let id: String
    let label: String

    static func resolve(_ options: [ModelOption], _ text: String) -> String {
        let value = text.trimmingCharacters(in: .whitespaces)
        return options.first {
            $0.label.caseInsensitiveCompare(value) == .orderedSame || $0.id.caseInsensitiveCompare(value) == .orderedSame
        }?.id ?? value
    }
}

/// Where the engine CLIs are looked up and the environment they run with. Apps opened from Finder
/// get a minimal PATH, so the login shell's PATH and the usual install folders are added.
struct EngineEnvironment: Sendable {
    let searchPath: [String]
    let variables: [String: String]

    init(shellPath: String?, base: [String: String] = ProcessInfo.processInfo.environment,
         home: String = NSHomeDirectory()) {
        let extra: [String] = [
            "\(home)/.local/bin", "\(home)/.claude/local", "/opt/homebrew/bin", "/usr/local/bin",
            "\(home)/.npm-global/bin", "\(home)/.bun/bin", "\(home)/.volta/bin", "\(home)/Library/pnpm",
            "\(home)/.codex/bin",
        ] + EngineEnvironment.nodeVersionFolders(home: home)
        let shell: [String] = (shellPath ?? "").split(separator: ":").map(String.init)
        let inherited: [String] = (base["PATH"] ?? "").split(separator: ":").map(String.init)
        let system: [String] = ["/usr/bin", "/bin", "/usr/sbin", "/sbin"]
        var seen = Set<String>()
        searchPath = (shell + inherited + extra + system).filter { !$0.isEmpty && seen.insert($0).inserted }
        var variables = base
        variables["PATH"] = searchPath.joined(separator: ":")
        variables["HOME"] = variables["HOME"] ?? home
        self.variables = variables
    }

    private static func nodeVersionFolders(home: String) -> [String] {
        let root = "\(home)/.nvm/versions/node"
        let versions = (try? FileManager.default.contentsOfDirectory(atPath: root)) ?? []
        return versions.sorted { $0.compare($1, options: .numeric) == .orderedDescending }.map { "\(root)/\($0)/bin" }
    }

    func resolve(_ provider: Provider) -> String? {
        guard provider != .none else { return nil }
        let name = provider == .claude ? "claude" : "codex"
        return searchPath.map { ($0 as NSString).appendingPathComponent(name) }
            .first { FileManager.default.isExecutableFile(atPath: $0) }
    }
}

enum EngineCli {
    static let timeout: TimeInterval = 60

    static func productName(_ provider: Provider) -> String { provider == .codex ? "Codex" : "Claude Code" }

    static func installPage(_ provider: Provider) -> URL {
        URL(string: provider == .codex ? "https://github.com/openai/codex" : "https://docs.anthropic.com/en/docs/claude-code")!
    }

    private static let signInProblem = try! NSRegularExpression(
        pattern: #"authenticat|not logged in|log ?in|sign ?in|oauth|\b401\b|unauthori[sz]ed|token (has )?expired|session expired"#,
        options: [.caseInsensitive])

    static func isSignInError(_ error: Error) -> Bool {
        guard let error = error as? EngineError, !error.message.contains("was not found") else { return false }
        let range = NSRange(error.message.startIndex..., in: error.message)
        return signInProblem.firstMatch(in: error.message, range: range) != nil
    }

    static func checkSignIn(_ provider: Provider, environment: EngineEnvironment) async -> SignIn {
        guard let executable = environment.resolve(provider) else { return .notInstalled }
        do {
            let result = try await ProcessRunner.run(executable, provider == .claude ? ["auth", "status"] : ["login", "status"],
                                                     input: "", directory: NSTemporaryDirectory(),
                                                     environment: environment.variables, timeout: 20)
            return provider == .claude ? parseClaudeSignIn(result) : parseCodexSignIn(result)
        } catch {
            ErrorLog.write("Could not check \(productName(provider)) sign-in", error)
            return .unknown
        }
    }

    static func parseClaudeSignIn(_ result: ProcessResult) -> SignIn {
        guard let data = result.output.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return result.exitCode == 0 ? .unknown : .signedOut
        }
        return json["loggedIn"] as? Bool == true ? .signedIn : .signedOut
    }

    static func parseCodexSignIn(_ result: ProcessResult) -> SignIn {
        let text = result.output + "\n" + result.error
        if text.range(of: "Not logged in", options: .caseInsensitive) != nil { return .signedOut }
        if result.exitCode == 0 && text.range(of: "Logged in", options: .caseInsensitive) != nil { return .signedIn }
        return result.exitCode == 0 ? .unknown : .signedOut
    }

    /// A Terminal script that signs in interactively, because both CLIs sign in through the browser.
    static func signInScript(executable: String, provider: Provider, path: String) -> String {
        let command = provider == .claude ? "auth login" : "login"
        return """
        #!/bin/zsh
        export PATH=\(shellQuote(path))
        \(shellQuote(executable)) \(command)
        echo
        read -k 1 "?Press any key to close this window."
        exit

        """
    }

    static func shellQuote(_ text: String) -> String { "'" + text.replacingOccurrences(of: "'", with: "'\\''") + "'" }

    static func claudeArguments(model: String, instructions: String) -> [String] {
        ["-p", "--model", model, "--system-prompt", instructions,
         "--safe-mode", "--strict-mcp-config", "--tools", "", "--no-session-persistence", "--effort", "low"]
    }

    static func codexArguments(model: String, workDirectory: String, outputFile: String) -> [String] {
        ["exec", "--ephemeral", "--skip-git-repo-check", "--ignore-user-config", "--ignore-rules",
         "--sandbox", "read-only", "--cd", workDirectory, "--model", model,
         "--config", "model_reasoning_effort=low", "--output-last-message", outputFile, "-"]
    }

    static func codexInput(instructions: String, text: String) -> String { instructions + "\n\n" + text }

    static func translate(_ engine: EngineChoice, instructions: String, text: String,
                          environment: EngineEnvironment) async throws -> String {
        let product = productName(engine.provider)
        guard let executable = environment.resolve(engine.provider) else {
            throw EngineError(message: "\(product) CLI was not found. Install it and sign in once.")
        }
        let work = FileManager.default.temporaryDirectory.appendingPathComponent("InlineTranslator-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        defer {
            do { try FileManager.default.removeItem(at: work) }
            catch { ErrorLog.write("Could not remove temporary folder", error) }
        }
        if engine.provider == .claude {
            let result = try await ProcessRunner.run(executable, claudeArguments(model: engine.model, instructions: instructions),
                                                     input: text, directory: work.path, environment: environment.variables,
                                                     timeout: timeout)
            if result.exitCode != 0 {
                throw EngineError(message: describe(product, result.exitCode, lastLine(result.output + "\n" + result.error)))
            }
            return result.output
        }
        let outputFile = work.appendingPathComponent("translation.txt")
        let result = try await ProcessRunner.run(executable, codexArguments(model: engine.model, workDirectory: work.path,
                                                                            outputFile: outputFile.path),
                                                 input: codexInput(instructions: instructions, text: text),
                                                 directory: work.path, environment: environment.variables, timeout: timeout)
        // Codex echoes the conversation to stderr, so only its ERROR lines may be surfaced or logged.
        guard result.exitCode == 0, let translated = try? String(contentsOf: outputFile, encoding: .utf8) else {
            throw EngineError(message: describe(product, result.exitCode, codexError(result.error)))
        }
        return translated
    }

    static func codexError(_ stderr: String) -> String {
        guard let line = stderr.components(separatedBy: "\n").map({ $0.trimmingCharacters(in: .whitespaces) })
            .last(where: isCodexErrorLine) else { return "" }
        guard let start = line.range(of: "\"message\":\"") else {
            // Tracing lines read "<timestamp> ERROR <module>: <message>"; keep only the message.
            guard let match = codexTracingError.firstMatch(in: line, range: NSRange(line.startIndex..., in: line)),
                  let message = Range(match.range(at: 1), in: line) else { return truncate(line) }
            return truncate(String(line[message]))
        }
        let rest = line[start.upperBound...]
        return truncate(String(rest.prefix { $0 != "\"" }))
    }

    private static let codexTracingError = try! NSRegularExpression(
        pattern: #"^\d{4}-\d{2}-\d{2}T[0-9:.]+Z\s+ERROR\s+(?:[A-Za-z0-9_]+(?:::[A-Za-z0-9_]+)*:\s+)?(.*)$"#)

    private static func isCodexErrorLine(_ line: String) -> Bool {
        line.hasPrefix("ERROR") || codexTracingError.firstMatch(in: line, range: NSRange(line.startIndex..., in: line)) != nil
    }

    static let claudeModels = [
        ModelOption(id: "claude-haiku-5-5", label: "Haiku 5.5"),
        ModelOption(id: "claude-sonnet-5-5", label: "Sonnet 5.5"),
        ModelOption(id: "claude-opus-5-5", label: "Opus 5.5"),
        ModelOption(id: "claude-fable-5", label: "Fable 5"),
    ]

    /// Codex models offered even when the CLI's model cache is older than them or missing.
    static let builtInCodexModels = [
        ModelOption(id: "gpt-6.1-terra", label: "GPT-6.1-Terra"),
        ModelOption(id: AppSettings.defaultCodexModel, label: "GPT-5.6-Luna"),
    ]

    static func suggestedModels(_ provider: Provider) -> [ModelOption] {
        switch provider {
        case .claude: claudeModels
        case .codex: mergeCodexModels(cached: cachedCodexModels)
        case .none: []
        }
    }

    /// Newer built-in models go first, the default model last, each only when the cache does not list it.
    static func mergeCodexModels(cached: [ModelOption]) -> [ModelOption] {
        let missing = builtInCodexModels.filter { model in !cached.contains { $0.id == model.id } }
        return missing.filter { $0.id != AppSettings.defaultCodexModel } + cached
            + missing.filter { $0.id == AppSettings.defaultCodexModel }
    }

    static func parseCodexModels(_ data: Data) -> [ModelOption] {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let models = json["models"] as? [[String: Any]] else { return [] }
        var seen = Set<String>()
        return models.compactMap { model in
            if let visibility = model["visibility"], visibility as? String != "list" { return nil }
            guard let id = model["slug"] as? String, AppSettings.isValidModel(id), seen.insert(id).inserted else { return nil }
            let label = (model["display_name"] as? String).flatMap { $0.trimmingCharacters(in: .whitespaces).isEmpty ? nil : $0 }
            return ModelOption(id: id, label: label ?? id)
        }
    }

    private static let cachedCodexModels: [ModelOption] = {
        let environment = ProcessInfo.processInfo.environment
        let home = environment["CODEX_HOME"].flatMap { $0.isEmpty ? nil : $0 } ?? NSHomeDirectory() + "/.codex"
        guard let data = FileManager.default.contents(atPath: home + "/models_cache.json") else { return [] }
        return parseCodexModels(data)
    }()

    private static func describe(_ product: String, _ exitCode: Int32, _ detail: String) -> String {
        detail.isEmpty ? "\(product) exited with code \(exitCode)." : "\(product): \(detail)"
    }

    private static func lastLine(_ text: String) -> String {
        truncate(text.components(separatedBy: "\n").map { $0.trimmingCharacters(in: .whitespaces) }.last { !$0.isEmpty } ?? "")
    }

    private static func truncate(_ text: String) -> String { text.count <= 300 ? text : String(text.prefix(300)) + "…" }
}
