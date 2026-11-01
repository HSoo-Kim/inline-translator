import CoreGraphics
import Foundation
import Security

struct Failure: Error, CustomStringConvertible { let description: String }

func check(_ condition: Bool, _ message: String, line: Int = #line) throws {
    if !condition { throw Failure(description: "line \(line): \(message)") }
}

func equal<T: Equatable>(_ expected: T, _ actual: T, line: Int = #line) throws {
    if expected != actual { throw Failure(description: "line \(line): expected <\(expected)> but was <\(actual)>") }
}

func expectError<E: Error>(_ type: E.Type, line: Int = #line, _ body: () async throws -> Void) async throws -> E {
    do { try await body() } catch let error as E { return error } catch {
        throw Failure(description: "line \(line): expected \(E.self) but got \(Swift.type(of: error)): \(error)")
    }
    throw Failure(description: "line \(line): expected \(E.self)")
}

final class Calls: @unchecked Sendable {
    private let lock = NSLock()
    private var items: [String] = []
    func add(_ item: String) { lock.lock(); items.append(item); lock.unlock() }
    var all: [String] { lock.lock(); defer { lock.unlock() }; return items }
}

let testDirectory = FileManager.default.temporaryDirectory.appendingPathComponent("InlineTranslator.Tests-\(UUID().uuidString)")
try FileManager.default.createDirectory(at: testDirectory, withIntermediateDirectories: true)
ErrorLog.fileURL = testDirectory.appendingPathComponent("error.log")
let sourcesDirectory = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : nil
let en = Languages.find("en")!, ko = Languages.find("ko")!, ja = Languages.find("ja")!, es = Languages.find("es")!

func settings(_ change: (inout AppSettings) -> Void) -> AppSettings {
    var value = AppSettings()
    change(&value)
    return value.normalized()
}

func run(_ executable: String, _ arguments: [String]) throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: executable)
    process.arguments = arguments
    process.standardOutput = FileHandle.nullDevice
    try process.run()
    process.waitUntilExit()
    try check(process.terminationStatus == 0, "\(executable) \(arguments.joined(separator: " ")) failed")
}

func waitForFile(_ url: URL) async throws -> String {
    for _ in 0..<500 {
        if let text = try? String(contentsOf: url, encoding: .utf8), text.hasSuffix("\n") { return text }
        try await Task.sleep(nanoseconds: 20_000_000)
    }
    throw Failure(description: "\(url.lastPathComponent) never appeared")
}

func alive(_ pid: pid_t) -> Bool { kill(pid, 0) == 0 }

let tests: [(String, () async throws -> Void)] = [
    ("hotkeys parse, normalize and format", {
        let hotkey = Hotkey.parse("ctrl+option+t")
        try equal("Ctrl+Option+T", hotkey?.description)
        try equal("⌃⌥T", hotkey?.symbols)
        try equal(0x11, hotkey?.keyCode)
        try equal(0x1800, hotkey?.modifiers.carbonFlags)
        try equal("Shift+Cmd+F5", Hotkey.parse(" Cmd + Shift + F5 ")?.description)
        try equal(0x31, Hotkey.parse("Alt+Space")?.keyCode)
        try equal("⌃⌥⇧⌘↑", Hotkey.parse("⌘+⌥+⌃+⇧+Up")?.symbols)
        try equal(hotkey, Hotkey.parse(hotkey!.description))
        try equal("Ctrl+Option+X", Hotkey.display("Ctrl+Option+X") == "⌃⌥X" ? "Ctrl+Option+X" : "")
        try equal("⌃⌥/", Hotkey.display("⌃⌥/"))
    }),
    ("invalid hotkeys are rejected", {
        for text in ["", "T", "Shift+A", "Ctrl+", "Ctrl+Ctrl+A", "Ctrl+A+B", "Ctrl+Tab", "Ctrl+Option+Ä", "Hyper+A", "Esc"] {
            try check(Hotkey.parse(text) == nil, "should reject: \(text)")
        }
        try check(!Hotkey(modifiers: .shift, keyCode: 0).isValid, "Shift alone is not enough")
    }),
    ("defaults: Ctrl+Option+T to English, Claude Haiku then Codex Luna", {
        let value = AppSettings().normalized()
        try equal(3, value.presets.count)
        try equal(HotkeyPreset(enabled: true, hotkey: "Ctrl+Option+T", targetLanguage: "en"), value.presets[0])
        try check(!value.presets[1].enabled && !value.presets[2].enabled, "only the first preset is on")
        try equal(EngineChoice(provider: .claude, model: "claude-haiku-5-5"), value.primary)
        try equal(EngineChoice(provider: .codex, model: "gpt-5.6-luna"), value.fallback)
        try check(value.wholeInputApps.contains("com.kakao.KakaoTalkMac"), "KakaoTalk translates the whole input")
        try check(value.checkForUpdates && value.autoInstallUpdates, "updates on by default")
        try equal("mouse", value.bubblePosition)
        try equal("light", value.bubbleTheme)
        try equal("", value.homeLanguage)
        try equal("Ctrl+Option+R", value.readingHotkey)
        try equal("en", value.readingTargetLanguage)
        try check(value.useContext, "surrounding text is considered by default")
    }),
    ("normalization repairs presets, engines, tone and apps", {
        let value = settings {
            $0.presets = [HotkeyPreset(enabled: true, hotkey: "cmd+shift+e", targetLanguage: "XX"),
                          HotkeyPreset(enabled: true, hotkey: "Cmd+Shift+E", targetLanguage: "ja")]
            $0.primary = EngineChoice(provider: .none, model: "")
            $0.fallback = EngineChoice(provider: .codex, model: "bad model!")
            $0.tone = "shouty"
            $0.uiLanguage = "JA"
            $0.homeLanguage = "xx"
            $0.bubbleTheme = "neon"
            $0.bubblePosition = "corner"
            $0.wholeInputApps = [" com.hnc.Discord ", "COM.HNC.DISCORD", "", "bad/id", String(repeating: "a", count: 300)]
        }
        try equal(HotkeyPreset(enabled: true, hotkey: "Shift+Cmd+E", targetLanguage: "en"), value.presets[0])
        try check(!value.presets[1].enabled, "duplicate hotkey disabled")
        try equal(HotkeyPreset(enabled: false, hotkey: "Ctrl+Option+K", targetLanguage: "ko"), value.presets[2])
        try equal(EngineChoice(provider: .claude, model: "claude-haiku-5-5"), value.primary)
        try equal(EngineChoice(provider: .codex, model: "gpt-5.6-luna"), value.fallback)
        try equal("natural", value.tone)
        try equal("ja", value.uiLanguage)
        try equal("", value.homeLanguage)
        try equal("light", value.bubbleTheme)
        try equal("mouse", value.bubblePosition)
        try equal(["com.hnc.Discord"], value.wholeInputApps)
        let same = settings { $0.fallback = $0.primary }
        try equal(EngineChoice.none, same.fallback)
    }),
    ("settings roundtrip atomically with Unicode", {
        let url = testDirectory.appendingPathComponent("nested/설정.json")
        let value = settings {
            $0.homeLanguage = "ko"
            $0.primary = EngineChoice(provider: .codex, model: "gpt-6-luna")
            $0.fallback = EngineChoice(provider: .claude, model: "claude-opus-5-5")
            $0.wholeInputApps = ["com.kakao.KakaoTalkMac"]
            $0.presets[1] = HotkeyPreset(enabled: true, hotkey: "Ctrl+Option+J", targetLanguage: "ja")
        }
        try SettingsStore.save(url, value)
        try equal(value, SettingsStore.load(url))
        let leftovers = try FileManager.default.contentsOfDirectory(atPath: url.deletingLastPathComponent().path)
        try equal(["설정.json"], leftovers)
    }),
    ("missing, corrupt and empty settings files", {
        try check(SettingsStore.load(testDirectory.appendingPathComponent("missing.json")) == nil, "missing file is first run")
        let url = testDirectory.appendingPathComponent("broken.json")
        for content in ["{oops", "null", "[]", "{}"] {
            try content.write(to: url, atomically: true, encoding: .utf8)
            try equal(AppSettings().normalized(), SettingsStore.load(url))
        }
        try #"{"tone":"formal","presets":"wrong"}"#.write(to: url, atomically: true, encoding: .utf8)
        let partial = SettingsStore.load(url)!
        try equal("formal", partial.tone)
        try equal(AppSettings.defaultPresets.map(\.hotkey), partial.presets.map(\.hotkey))
    }),
    ("settings drafts compare field values", {
        let original = AppSettings().normalized()
        var draft = original
        try equal(original, draft)
        draft.wholeInputApps = Array(original.wholeInputApps)
        try equal(original, draft)
        draft.presets[0].enabled = false
        try check(draft != original, "toggle detected")
        draft.presets[0].enabled = true
        try equal(original, draft)
    }),
    ("hotkey draft validation locates every enabled error without repairing it", {
        var draft = AppSettings().normalized()
        draft.presets[0].hotkey = ""
        draft.presets[1] = HotkeyPreset(enabled: true, hotkey: "T", targetLanguage: "ja")
        draft.presets[2] = HotkeyPreset(enabled: true, hotkey: "Ctrl+Option+R", targetLanguage: "ko")
        let errors = SettingsValidation.hotkeyErrors(draft)
        try equal(.missing, errors[0])
        try equal(.invalid, errors[1])
        try equal(.duplicate, errors[2])
        try equal(.duplicate, errors[AppSettings.presetCount])
        draft.readingHotkeyEnabled = false
        try check(SettingsValidation.hotkeyErrors(draft)[2] == nil, "disabled hotkeys are ignored")
        try equal("T", draft.presets[1].hotkey)
    }),
    ("prompts name the target and stay shell-safe for every language", {
        for language in Languages.all {
            for tone in AppSettings.tones {
                let prompt = Prompt.instructions(language, tone: tone)
                try check(prompt.contains("into \(language.englishName)."), "target named: \(language.code)")
                let twoWay = Prompt.instructions(language, tone: tone, home: language.code == "ko" ? en : ko)
                for value in [prompt, twoWay] {
                    try check(value.rangeOfCharacter(from: CharacterSet(charactersIn: "\"&|<>^%\n`$")) == nil, "shell-safe: \(language.code)")
                }
            }
        }
        try check(Prompt.instructions(en, tone: "casual").contains("casual"), "casual tone")
        try check(Prompt.instructions(en, tone: "formal").contains("formal"), "formal tone")
        let twoWay = Prompt.instructions(en, tone: "natural", home: ko)
        try check(twoWay.contains("If it is Korean, translate it into English."), "my language goes to the hotkey language")
        try check(twoWay.contains("such as English or Japanese, translate it into Korean."), "everything else comes home")
        try check(Prompt.instructions(en, tone: "natural", home: en).contains("return it unchanged"), "same home and target keeps text")
        try equal(Languages.all.count, Set(Languages.all.map(\.code)).count)
        try check(Languages.find("ZH-cn")?.code == "zh-CN" && Languages.find("xx") == nil, "language lookup")
    }),
    ("system languages map to supported languages", {
        try equal("ko", Languages.fromIdentifier("ko-KR").code)
        try equal("zh-TW", Languages.fromIdentifier("zh-Hant-TW").code)
        try equal("zh-TW", Languages.fromIdentifier("zh-HK").code)
        try equal("zh-CN", Languages.fromIdentifier("zh-Hans-CN").code)
        try equal("pt", Languages.fromIdentifier("pt-BR").code)
        try equal("en", Languages.fromIdentifier("sw-KE").code)
        try equal("ja", Languages.fromIdentifier("ja_JP").code)
    }),
    ("capture uses selection first and whole input only in listed apps", {
        let apps = ["ru.keepcoder.Telegram", "com.kakao.KakaoTalkMac"]
        try equal(.selection, Capture.decide(selectionCopied: true, appID: "com.microsoft.Word", wholeInputApps: apps))
        try equal(.selection, Capture.decide(selectionCopied: true, appID: "ru.keepcoder.Telegram", wholeInputApps: apps))
        try equal(.wholeInput, Capture.decide(selectionCopied: false, appID: "RU.KEEPCODER.telegram", wholeInputApps: apps))
        try equal(.needSelection, Capture.decide(selectionCopied: false, appID: "com.microsoft.Word", wholeInputApps: apps))
        try equal(.needSelection, Capture.decide(selectionCopied: false, appID: nil, wholeInputApps: apps))
        try equal(.needSelection, Capture.decide(selectionCopied: false, appID: "ru.keepcoder.Telegram", wholeInputApps: apps, reading: true))

        try check(!Capture.looksLikeWholeDocument("응! 근데 몇 시에 시작해?\n조금 늦을 것 같아"), "chat message passes")
        try check(Capture.looksLikeWholeDocument(String(repeating: "가", count: Capture.wholeInputMaxCharacters + 1)), "very long text")
        let lines = Array(repeating: "line", count: Capture.wholeInputMaxLines)
        try check(Capture.looksLikeWholeDocument((lines + ["line"]).joined(separator: "\n")), "many lines is a document")
        try check(!Capture.looksLikeWholeDocument(lines.joined(separator: "\r\n")), "line limit is inclusive with CRLF")
        try check(AppCatalog.isKnownChatApp("COM.kakao.KakaoTalkMac") && !AppCatalog.isKnownChatApp("com.microsoft.Word"), "known chat apps")

        try check(Capture.pasteWasIgnored("안녕하세요\r\n반가워요", "안녕하세요\n반가워요 "), "same selection after paste means read-only")
        try check(!Capture.pasteWasIgnored("안녕하세요", nil), "nothing selected after paste means it worked")
        try check(!Capture.pasteWasIgnored("안녕하세요", "Hello"), "different text means the paste replaced it")
        try check(!Capture.pasteWasIgnored("안녕하세요", ""), "empty copy means nothing is selected")
    }),
    ("input protection rejects changed selections and meaningful input", {
        try check(Capture.selectionUnchanged("one\r\ntwo", "one\ntwo"), "newline conversion is harmless")
        try check(!Capture.selectionUnchanged("old", "new"), "edited text rejected")
        try check(!Capture.selectionUnchanged("old", nil), "lost selection rejected")
        try check(!Capture.selectionUnchanged("old ", "old"), "whitespace changes are not ignored")
        try check(Capture.inputInvalidates(.keyDown, userData: 0), "key press invalidates replacement")
        try check(!Capture.inputInvalidates(.keyDown, userData: Capture.syntheticInputTag), "our own Cmd+C/V do not invalidate")
        try check(Capture.inputInvalidates(.keyDown, userData: 0x1234), "other injected input still invalidates")
        try check(Capture.inputInvalidates(.leftMouseDown, userData: Capture.syntheticInputTag), "a click always invalidates")
        try check(Capture.inputInvalidates(.rightMouseDown, userData: 0) && Capture.inputInvalidates(.otherMouseDown, userData: 0), "other buttons")
        try check(!Capture.inputInvalidates(.keyUp, userData: 0), "releasing the hotkey is harmless")
        try check(!Capture.inputInvalidates(.mouseMoved, userData: 0), "pointer movement is harmless")
        try check(!Capture.inputInvalidates(.flagsChanged, userData: 0), "modifier changes are harmless")
        try check(!Capture.inputInvalidates(.scrollWheel, userData: 0), "scrolling does not move the caret")
    }),
    ("reading hotkey normalizes and persists without conflicting with presets", {
        let duplicate = settings { $0.readingHotkey = "ctrl+option+t" }
        try check(!duplicate.readingHotkeyEnabled && duplicate.presets[0].enabled, "regular preset wins duplicate")
        let invalid = settings { $0.readingHotkey = "Esc" }
        try check(!invalid.readingHotkeyEnabled && invalid.readingHotkey.isEmpty, "bare Esc cannot be a reading shortcut")
        let disabledPreset = settings { $0.readingHotkey = "ctrl+option+j" }
        try check(disabledPreset.readingHotkeyEnabled, "disabled preset does not claim its key")
        try equal("Ctrl+Option+J", disabledPreset.readingHotkey)
        let url = testDirectory.appendingPathComponent("reading.json")
        try SettingsStore.save(url, disabledPreset.with(\.readingHotkeyEnabled, false).with(\.readingTargetLanguage, "ja"))
        let loaded = SettingsStore.load(url)!
        try equal("Ctrl+Option+J", loaded.readingHotkey)
        try equal("ja", loaded.readingTargetLanguage)
        try check(!loaded.readingHotkeyEnabled, "disabled choice survives saving")
    }),
    ("reading translation follows its selected language in both directions", {
        let inherited = settings { $0.presets = [HotkeyPreset(enabled: true, hotkey: "Ctrl+Option+T", targetLanguage: "ja")] }
        try equal("ja", inherited.readingTargetLanguage)
        try equal("ja", inherited.with(\.readingTargetLanguage, "xx").normalized().readingTargetLanguage)
        try equal("fr", inherited.with(\.readingTargetLanguage, "fr").normalized().readingTargetLanguage)
        for (original, output) in [("Hello", "ko"), ("안녕", "en"), ("こんにちは", "ko")] {
            let value = settings { $0.homeLanguage = "ko"; $0.readingTargetLanguage = "en" }
            let result = try await Translator { _, _, _ in "안녕" }.translate(value, target: en, text: original)
            try equal(output, result.output?.code)
        }
        let japanese = try await Translator { _, _, _ in "こんにちは" }
            .translate(inherited.with(\.homeLanguage, "ko"), target: ja, text: "안녕하세요")
        try equal("ja", japanese.output?.code)
    }),
    ("translator uses primary, then fallback, and reports both failures", {
        let value = settings { $0.homeLanguage = "ko" }
        let calls = Calls()
        let ok = try await Translator { engine, _, text in
            calls.add("\(engine.provider):\(text)")
            return " Hello \n"
        }.translate(value, target: en, text: "안녕")
        try equal("Hello", ok.text)
        try equal(.claude, ok.engine.provider)
        try check(ok.primaryError == nil, "no primary error")
        try equal(["claude:Translate this message into English.\nBEGIN MESSAGE\n안녕\nEND MESSAGE"], calls.all)

        let fallbackCalls = Calls()
        let fallback = try await Translator { engine, instructions, _ in
            fallbackCalls.add(engine.provider.rawValue)
            try check(instructions.contains("English"), "instructions passed to every engine")
            if engine.provider == .claude { throw EngineError(message: "login expired") }
            return "Hi"
        }.translate(value, target: en, text: "안녕")
        try equal("Hi", fallback.text)
        try equal(.codex, fallback.engine.provider)
        try equal("login expired", fallback.primaryError?.localizedDescription)
        try equal(["claude", "codex"], fallbackCalls.all)

        let both = try await expectError(TranslationFailed.self) {
            _ = try await Translator { engine, _, _ in throw EngineError(message: "\(engine.provider) down") }
                .translate(value, target: en, text: "x")
        }
        try equal("claude down", both.primary.localizedDescription)
        try equal("codex down", both.fallback?.localizedDescription)
        try equal("claude down · codex down", both.localizedDescription)
        let missingFallback = TranslationFailed(primary: EngineError(message: "Claude Code: Not logged in"),
                                                fallback: EngineError(message: "Codex CLI was not found. Install it and sign in once."))
        try equal("Claude Code: Not logged in", missingFallback.localizedDescription)

        let alone = try await expectError(TranslationFailed.self) {
            _ = try await Translator { _, _, _ in throw EngineError(message: "down") }
                .translate(value.with(\.fallback, .none), target: en, text: "x")
        }
        try check(alone.fallback == nil, "no fallback attempted")
        try equal("down", alone.localizedDescription)
    }),
    ("translator treats empty output as failure and lets cancellation through", {
        let single = AppSettings().normalized().with(\.fallback, .none)
        let empty = try await expectError(TranslationFailed.self) {
            _ = try await Translator { _, _, _ in "  \n" }.translate(single, target: en, text: "x")
        }
        try check(empty.primary is EmptyTranslation, "empty output is a failure")
        let calls = Calls()
        let task = Task {
            withUnsafeCurrentTask { $0?.cancel() }
            return try await Translator { _, _, _ in calls.add("run"); return "x" }.translate(single, target: en, text: "x")
        }
        _ = try await expectError(CancellationError.self) { _ = try await task.value }
        try equal(0, calls.all.count)
    }),
    ("cancellation stops primary and fallback without delivering a result", {
        for fallback in [false, true] {
            let calls = Calls()
            let task = Task {
                try await Translator { engine, _, _ in
                    calls.add(engine.provider.rawValue)
                    if fallback && engine.provider == .claude { throw EngineError(message: "offline") }
                    try await Task.sleep(nanoseconds: 30_000_000_000)
                    return "late"
                }.translate(settings { $0.homeLanguage = "ko" }, target: en, text: "안녕")
            }
            while calls.all.count < (fallback ? 2 : 1) { try await Task.sleep(nanoseconds: 5_000_000) }
            task.cancel()
            _ = try await expectError(CancellationError.self) { _ = try await task.value }
            try equal(fallback ? 2 : 1, calls.all.count)
        }
        let late = Task {
            try await Translator { _, _, _ in
                withUnsafeCurrentTask { $0?.cancel() }
                return "stale result"
            }.translate(AppSettings().normalized(), target: en, text: "안녕")
        }
        _ = try await expectError(CancellationError.self) { _ = try await late.value }
        let failsLate = Task {
            try await Translator { _, _, _ in
                withUnsafeCurrentTask { $0?.cancel() }
                throw EngineError(message: "process ended while cancelling")
            }.translate(AppSettings().normalized().with(\.fallback, .none), target: en, text: "안녕")
        }
        _ = try await expectError(CancellationError.self) { _ = try await failsLate.value }
    }),
    ("surrounding context keeps nearby text and rejects ranges that do not hold the copied text", {
        let text = "회의 안건입니다.\n지난주 결정: 배포는 금요일.\n그건 다음 주로 미룰게요.\n질문 있으면 알려 주세요."
        let selected = "그건 다음 주로 미룰게요."
        let range = (text as NSString).range(of: selected)
        let context = ContextWindow.around(text: text, selection: range, selected: selected)
        try equal(TranslationContext(before: "회의 안건입니다.\n지난주 결정: 배포는 금요일.", after: "질문 있으면 알려 주세요."), context)
        try equal(context, ContextWindow.around(text: text.replacingOccurrences(of: "\n", with: "\r\n"),
                                                selection: (text.replacingOccurrences(of: "\n", with: "\r\n") as NSString).range(of: selected),
                                                selected: selected))
        try check(ContextWindow.around(text: text, selection: NSRange(location: 0, length: 5), selected: selected) == nil, "other text rejected")
        try check(ContextWindow.around(text: text, selection: NSRange(location: 60, length: 10), selected: selected) == nil, "out of range rejected")
        try check(ContextWindow.around(text: selected, selection: NSRange(location: 0, length: (selected as NSString).length), selected: selected) == nil,
                  "nothing around the selection means no context")
        let long = String(repeating: "word ", count: 600) + "\n" + String(repeating: "앞 문장입니다. ", count: 80)
        let wide = long + "TARGET" + String(repeating: " 뒤 문장", count: 300)
        let clipped = ContextWindow.around(text: wide, selection: (wide as NSString).range(of: "TARGET"), selected: "TARGET")!
        try check((clipped.before as NSString).length <= ContextWindow.maxBefore && (clipped.after as NSString).length <= ContextWindow.maxAfter, "bounded")
        try check(clipped.before.hasPrefix("word") || clipped.before.hasPrefix("앞"), "cut at a word boundary: \(clipped.before.prefix(12))")
        try check(clipped.after.hasSuffix("뒤 문장"), "after cut at a word boundary")
        try check(ContextWindow.clip(before: " \n ", after: "") == nil, "whitespace is no context")
    }),
    ("PDF context finds the copied text on the page despite line breaks and picks the occurrence nearest the selection", {
        // Preview lists a page as lines, while a copy across lines joins them with a space.
        let page = "Notes\nThe model is\nfast. It trains\nin a day. Results follow.\nThe model is\nfast. It trains\nin a week. End."
        let first = ContextWindow.around(page: page, selected: "The model is fast. It trains in a")
        try equal(TranslationContext(before: "Notes", after: "day. Results follow.\nThe model is\nfast. It trains\nin a week. End."), first)
        let second = (page as NSString).range(of: "The model is", options: .backwards).location
        let near = ContextWindow.around(page: page, selected: "The model is fast. It trains in a", near: second)
        try equal(TranslationContext(before: "Notes\nThe model is\nfast. It trains\nin a day. Results follow.", after: "week. End."), near)
        try equal(NSRange(location: 10, length: 8), ContextWindow.locate("model\nis", in: "Notes The model is"))
        let unicode = "앞🌳\n결과는 아주 좋다.\n뒤"
        try equal((unicode as NSString).range(of: "결과는 아주 좋다."),
                  ContextWindow.locate("결과는아주\n좋다.", in: unicode))
        try equal(NSRange(location: 4, length: 5), ContextWindow.locate("ababa", in: "ababababa", near: 4))
        try check(ContextWindow.locate("aaaab", in: "aaaaaa") == nil, "partial matches do not count")
        let repeated = String(repeating: "a", count: 100_000)
        try equal(NSRange(location: 50_000, length: 50_000),
                  ContextWindow.locate(String(repeating: "a", count: 50_000), in: repeated, near: 50_000))
        try check(ContextWindow.around(page: page, selected: "not on this page") == nil, "missing text gives no context")
        try check(ContextWindow.around(page: page, selected: " \n") == nil, "blank selection gives no context")
    }),
    ("CLI arguments keep sessions unsaved and tools disabled", {
        let claude = EngineCli.claudeArguments(model: "haiku", instructions: "Translate.")
        try check(claude.contains("--no-session-persistence") && claude.contains("--safe-mode") && claude.contains("--strict-mcp-config"), "claude isolation flags")
        try equal("", claude[claude.firstIndex(of: "--tools")! + 1])
        try equal("haiku", claude[claude.firstIndex(of: "--model")! + 1])
        let codex = EngineCli.codexArguments(model: "gpt-5.6-luna", workDirectory: "/tmp/work", outputFile: "/tmp/work/out.txt")
        try check(codex.contains("--ephemeral") && codex.contains("--ignore-user-config"), "codex keeps no session and ignores user hooks")
        try equal("read-only", codex[codex.firstIndex(of: "--sandbox")! + 1])
        try equal("-", codex.last)
        try check(EngineCli.codexInput(instructions: "Do it.", text: "안녕").hasSuffix("\n\n안녕"), "message appended after instructions")
        try equal("Hello\nthere", Translator.unwrap("BEGIN MESSAGE\r\nHello\nthere\nEND MESSAGE\n"))
    }),
    ("Codex model cache parsing", {
        let json = #"""
        {"models":[{"slug":"gpt-6.1-sol","display_name":"GPT-6.1-Sol","visibility":"list"},{"slug":"codex-auto-review","visibility":"hide"},
        {"slug":"gpt-5.6-luna"},{"slug":"bad slug","visibility":"list"},{"visibility":"list"},{"slug":"gpt-6.1-sol","visibility":"list"}]}
        """#
        let models = EngineCli.parseCodexModels(Data(json.utf8))
        try equal(["gpt-6.1-sol", "gpt-5.6-luna"], models.map(\.id))
        try equal(["GPT-6.1-Sol", "gpt-5.6-luna"], models.map(\.label))
        try equal(0, EngineCli.parseCodexModels(Data("{oops".utf8)).count)
        try equal(0, EngineCli.parseCodexModels(Data(#"{"models":{}}"#.utf8)).count)
        try equal(["gpt-6.1-terra", "gpt-6-sol", "gpt-5.6-luna"],
                  EngineCli.mergeCodexModels(cached: [ModelOption(id: "gpt-6-sol", label: "GPT-6-Sol")]).map(\.id))
        try equal(["gpt-5.6-luna", "gpt-6.1-terra"],
                  EngineCli.mergeCodexModels(cached: [ModelOption(id: "gpt-5.6-luna", label: "GPT-5.6-Luna"),
                                                      ModelOption(id: "gpt-6.1-terra", label: "GPT-6.1-Terra")]).map(\.id))
        try equal(["gpt-6.1-terra", "gpt-5.6-luna"], EngineCli.mergeCodexModels(cached: []).map(\.id))
        try check(EngineCli.suggestedModels(.codex).contains { $0.id == "gpt-6.1-terra" && $0.label == "GPT-6.1-Terra" }, "6.1 Terra offered")
    }),
    ("model labels resolve to model IDs", {
        try equal(["Haiku 5.5", "Sonnet 5.5", "Opus 5.5", "Fable 5"], EngineCli.claudeModels.map(\.label))
        try equal("claude-sonnet-5-5", ModelOption.resolve(EngineCli.claudeModels, " sonnet 5.5 "))
        try equal("claude-opus-5-5", ModelOption.resolve(EngineCli.claudeModels, "claude-opus-5-5"))
        try equal("claude-haiku-4-5", ModelOption.resolve(EngineCli.claudeModels, "claude-haiku-4-5"))
        try equal("Claude · Haiku 5.5", EngineChoice(provider: .claude, model: "claude-haiku-5-5").displayName)
        try equal("Claude · opus", EngineChoice(provider: .claude, model: "opus").displayName)
    }),
    ("sign-in status parsing, sign-in errors and the Terminal script", {
        try equal(.signedIn, EngineCli.parseClaudeSignIn(ProcessResult(exitCode: 0, output: #"{"loggedIn":true,"authMethod":"claude.ai"}"#, error: "")))
        try equal(.signedOut, EngineCli.parseClaudeSignIn(ProcessResult(exitCode: 1, output: #"{"loggedIn":false}"#, error: "")))
        try equal(.signedOut, EngineCli.parseClaudeSignIn(ProcessResult(exitCode: 1, output: "not json", error: "")))
        try equal(.unknown, EngineCli.parseClaudeSignIn(ProcessResult(exitCode: 0, output: "not json", error: "")))
        try equal(.signedIn, EngineCli.parseCodexSignIn(ProcessResult(exitCode: 0, output: "Logged in using ChatGPT\n", error: "")))
        try equal(.signedOut, EngineCli.parseCodexSignIn(ProcessResult(exitCode: 1, output: "", error: "Not logged in\n")))
        try equal(.signedOut, EngineCli.parseCodexSignIn(ProcessResult(exitCode: 0, output: "Not logged in", error: "")))

        try check(EngineCli.isSignInError(EngineError(message: "Claude Code: Failed to authenticate: OAuth session expired")), "expired OAuth")
        try check(EngineCli.isSignInError(EngineError(message: "Codex: 401 Unauthorized")), "401")
        try check(EngineCli.isSignInError(EngineError(message: "Codex: Not logged in. Run codex login.")), "not logged in")
        try check(!EngineCli.isSignInError(EngineError(message: "Codex CLI was not found. Install it and sign in once.")), "missing CLI")
        try check(!EngineCli.isSignInError(EngineError(message: "Codex: The 'gpt-6-luna' model is not supported.")), "model errors")
        try check(!EngineCli.isSignInError(EngineTimeout(message: "login took too long")), "only engine errors count")

        let script = EngineCli.signInScript(executable: "/Users/me/My Tools/claude", provider: .claude, path: "/usr/bin:/it's/bin")
        try check(script.contains("'/Users/me/My Tools/claude' auth login\n"), "claude login command quoted")
        try check(script.contains("export PATH='/usr/bin:/it'\\''s/bin'"), "PATH quoted")
        try check(EngineCli.signInScript(executable: "/bin/codex", provider: .codex, path: "").contains("'/bin/codex' login\n"), "codex login")
    }),
    ("app catalog keeps real apps and merges running ones", {
        let merged = AppCatalog.merge(
            installed: [
                AppEntry(bundleID: "com.kakao.KakaoTalkMac", name: "카카오톡", path: "/Applications/KakaoTalk.app", running: false),
                AppEntry(bundleID: "com.kakao.kakaotalkmac", name: "카카오톡 (두 번째 복사본)", path: "/Users/me/Applications/KakaoTalk.app", running: false),
                AppEntry(bundleID: "com.apple.TextEdit", name: "TextEdit", path: nil, running: false),
                AppEntry(bundleID: "io.github.hsoo-kim.inline-translator", name: "Inline Translator", path: nil, running: false),
            ],
            running: [
                AppEntry(bundleID: "ru.keepcoder.Telegram", name: "Telegram", path: "/Applications/Telegram.app", running: true),
                AppEntry(bundleID: "COM.KAKAO.KAKAOTALKMAC", name: "채팅", path: nil, running: true),
                AppEntry(bundleID: "bad id", name: "Broken", path: nil, running: true),
            ],
            excluding: ["io.github.hsoo-kim.inline-translator"])
        try equal(["com.kakao.kakaotalkmac", "ru.keepcoder.telegram", "com.apple.textedit"], merged.map(\.id))
        let kakao = merged[0]
        try equal("카카오톡", kakao.name)
        try check(kakao.running && kakao.path == "/Applications/KakaoTalk.app", "installed entry marked running")
    }),
    ("my language goes out, everything else comes home", {
        try equal(en, Direction.resolve("내일 전까지 첨부한 슬라이드 검토 부탁드려요.", target: en, home: ko))
        try equal(ko, Direction.resolve("Please review the attached slides.", target: en, home: ko))
        try equal(ko, Direction.resolve("明日までに添付のスライドを確認してください。", target: en, home: ko))
        try equal(ko, Direction.resolve("Por favor revisa las diapositivas.", target: en, home: ko))
        try equal(en, Direction.resolve("Thanks for today! 내일 회의 자료는 오후 3시까지 보내드릴게요.", target: en, home: ko))
        try equal(ko, Direction.resolve("请明天之前查看幻灯片。", target: en, home: ko))
        try equal(en, Direction.resolve("明日までに確認", target: en, home: ja))
        try equal(ja, Direction.resolve("请明天之前查看幻灯片。", target: en, home: ja))
        try equal(en, Direction.resolve("안녕하세요", target: es, home: en))
        try check(Direction.resolve("Hola, ¿cómo estás?", target: es, home: en) == nil, "Latin with a Latin home language is left to the model")
        try equal(en, Direction.resolve("12345 !!!", target: en, home: ko))
        try equal(ko, Direction.resolve("Hi", target: ko, home: ko))
        try equal(.kana, Direction.dominant("東京駅で待ち合わせしましょう"))
        try equal(.han, Direction.dominant("东京站见面"))
    }),
    ("bubble sits right under the selection, or above it near the screen bottom", {
        let union = BubbleLayout.union([CGRect(x: 100, y: 200, width: 300, height: 20), CGRect(x: 80, y: 222, width: 120, height: 20)])
        try equal(CGRect(x: 80, y: 200, width: 320, height: 42), union)
        try check(BubbleLayout.union([]) == nil, "no boxes means unknown selection")
        let area = CGRect(x: 0, y: 25, width: 1920, height: 1015)
        let size = CGSize(width: 400, height: 100)
        try equal(CGPoint(x: 80, y: 250), BubbleLayout.placement(target: union!, size: size, area: area, gap: 8))
        try equal(CGPoint(x: 300, y: 872), BubbleLayout.placement(target: CGRect(x: 300, y: 980, width: 200, height: 20), size: size, area: area, gap: 8))
        try equal(CGPoint(x: 1512, y: 128), BubbleLayout.placement(target: CGRect(x: 1800, y: 100, width: 100, height: 20), size: size, area: area, gap: 8))
        try equal(CGPoint(x: 8, y: 33), BubbleLayout.placement(target: CGRect(x: -50, y: 30, width: 10, height: 970), size: size, area: area, gap: 8))
    }),
    ("Codex errors expose only the ERROR message", {
        let json = "user\n안녕하세요 비밀 메시지\nERROR: {\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'gpt-6-luna' model is not supported.\"}}\n"
        try equal("The 'gpt-6-luna' model is not supported.", EngineCli.codexError(json))
        try equal("", EngineCli.codexError("user\n안녕하세요 비밀 메시지 ERROR here\n"))
        let tracing = """
        2026-11-01T07:26:32.781124Z ERROR codex_login::auth::manager: Failed to refresh token status=401 Unauthorized
        user
        비밀 메시지
        2026-11-01T07:26:33.072396Z ERROR codex_core::exec: Your access token could not be refreshed. Please sign in again.
        """
        let message = EngineCli.codexError(tracing)
        try equal("Your access token could not be refreshed. Please sign in again.", message)
        try check(EngineCli.isSignInError(EngineError(message: "Codex: " + message)), "expired Codex login is a sign-in problem")
    }),
    ("engine lookup searches the login shell PATH and common install folders", {
        let bin = testDirectory.appendingPathComponent("shell bin")
        try FileManager.default.createDirectory(at: bin, withIntermediateDirectories: true)
        let claude = bin.appendingPathComponent("claude")
        try "#!/bin/sh\n".write(to: claude, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: claude.path)
        let environment = EngineEnvironment(shellPath: bin.path + ":/usr/bin", base: ["PATH": "/usr/bin:/bin"], home: testDirectory.path)
        try equal(claude.path, environment.resolve(.claude))
        try equal(nil, environment.resolve(.none))
        try check(environment.searchPath.first == bin.path, "shell PATH first")
        try equal(1, environment.searchPath.filter { $0 == "/usr/bin" }.count)
        try check(environment.searchPath.contains(testDirectory.path + "/.local/bin"), "~/.local/bin searched")
        try equal(environment.searchPath.joined(separator: ":"), environment.variables["PATH"])
        try equal("/usr/local/bin:/usr/bin", ShellPath.parse("Welcome!\n\n__IT_PATH__/usr/local/bin:/usr/bin__IT_END__"))
        try equal(nil, ShellPath.parse("no markers"))
        let zsh = await ShellPath.load(shell: "/bin/zsh")
        try check(zsh?.contains("/usr/bin") == true, "login shell PATH read")
    }),
    ("process runner passes stdin and returns exit codes", {
        let sorted = try await ProcessRunner.run("/usr/bin/sort", [], input: "beta\nalpha\n", directory: testDirectory.path,
                                                 environment: [:], timeout: 30)
        try equal(0, sorted.exitCode)
        try equal("alpha\nbeta\n", sorted.output)
        let failed = try await ProcessRunner.run("/bin/sh", ["-c", "echo oops >&2; exit 7"], input: "", directory: testDirectory.path,
                                                 environment: [:], timeout: 30)
        try equal(7, failed.exitCode)
        try equal("oops\n", failed.error)
        let large = String(repeating: "가나다라마바사\n", count: 40000)
        let echoed = try await ProcessRunner.run("/bin/cat", [], input: large, directory: testDirectory.path, environment: [:], timeout: 30)
        try equal(large, echoed.output)
        let early = try await ProcessRunner.run("/usr/bin/true", [], input: large, directory: testDirectory.path, environment: [:], timeout: 30)
        try equal(0, early.exitCode)
        let directory = try await ProcessRunner.run("/bin/pwd", [], input: "", directory: testDirectory.path, environment: [:], timeout: 30)
        try check(directory.output.hasSuffix(testDirectory.lastPathComponent + "\n"), "runs in the work folder")
    }),
    ("process runner kills the CLI on timeout", {
        let started = Date()
        let error = try await expectError(EngineTimeout.self) {
            _ = try await ProcessRunner.run("/bin/sleep", ["30"], input: "", directory: testDirectory.path, environment: [:], timeout: 1)
        }
        try check(Date().timeIntervalSince(started) < 10, "timed out promptly")
        try check(error.message.contains("sleep"), "timeout names the CLI")
    }),
    ("process runner cancels a running CLI and everything it started", {
        let pids = testDirectory.appendingPathComponent("pids")
        let task = Task {
            try await ProcessRunner.run("/bin/sh", ["-c", "sleep 30 & echo $$ $! > '\(pids.path)'; wait"], input: "",
                                        directory: testDirectory.path, environment: [:], timeout: 60)
        }
        let ids = try await waitForFile(pids).split(separator: " ").compactMap { pid_t($0.trimmingCharacters(in: .whitespacesAndNewlines)) }
        try equal(2, ids.count)
        task.cancel()
        _ = try await expectError(CancellationError.self) { _ = try await task.value }
        try await Task.sleep(nanoseconds: 200_000_000)
        try check(!alive(ids[0]), "the CLI exited")
        try check(!alive(ids[1]), "its child process exited")
    }),
    ("release parsing accepts only verifiable macOS packages", {
        let digest = String(repeating: "a", count: 64)
        let json = """
        [{"tag_name":"v0.1.17","html_url":"https://github.com/HSoo-Kim/inline-translator/releases/tag/v0.1.17",
          "assets":[{"name":"inline-translator-win-x64.zip","browser_download_url":"https://github.com/w.zip","digest":"sha256:\(digest)"}]},
         {"tag_name":"mac-v0.2.0","html_url":"https://github.com/HSoo-Kim/inline-translator/releases/tag/mac-v0.2.0",
          "assets":[{"name":"other.zip"},{"name":"\(UpdateChecker.packageName)","browser_download_url":"https://github.com/x/y.dmg","digest":"sha256:\(digest)"}]},
         {"tag_name":"mac-v0.3.0","draft":true,"html_url":"https://github.com/a"},
         {"tag_name":"mac-v0.4.0","prerelease":true,"html_url":"https://github.com/a"},
         {"tag_name":"mac-v0.1.5","html_url":"https://github.com/a"}]
        """
        let latest = try UpdateChecker.latest(in: Data(json.utf8))
        try equal(AppVersion(0, 2, 0), latest?.version)
        try equal("mac-v0.2.0", latest?.tag)
        try equal(digest, latest?.packageSHA256)
        try equal(URL(string: "https://github.com/x/y.dmg"), latest?.packageURL)
        func one(_ json: String) -> ReleaseInfo? {
            (try? JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any]).flatMap(UpdateChecker.parse)
        }
        try check(one(#"{"tag_name":"mac-v0.2.0","html_url":"https://github.com/a","assets":[{"name":"inline-translator-mac.dmg","browser_download_url":"https://github.com/x"}]}"#)?.packageURL == nil, "no digest means no automatic install")
        try check(one(#"{"tag_name":"mac-v0.2.0","html_url":"https://github.com/a","assets":[{"name":"inline-translator-mac.dmg","browser_download_url":"https://evil.example/x","digest":"sha256:\#(digest)"}]}"#)?.packageURL == nil, "non-GitHub download rejected")
        try check(one(#"{"tag_name":"mac-v0.2.0","html_url":"https://evil.example/"}"#) == nil, "non-GitHub page rejected")
        try check(one(#"{"tag_name":"v0.2.0","html_url":"https://github.com/a"}"#) == nil, "Windows tag ignored")
        try check(one(#"{"tag_name":"mac-latest","html_url":"https://github.com/a"}"#) == nil, "non-version tag rejected")
        try check(AppVersion("0.1") == AppVersion(0, 1, 0) && AppVersion("1.2.x") == nil && AppVersion("-1.0") == nil, "versions")
        try check(AppVersion(0, 10, 0) > AppVersion(0, 9, 9), "numeric comparison")
        try equal(300, UpdateChecker.checkInterval)
    }),
    ("update installer replaces the app only after a SHA-256 match", {
        let folder = testDirectory.appendingPathComponent("update")
        let manager = FileManager.default
        func makeApp(_ url: URL, id: String, content: String) throws {
            try manager.createDirectory(at: url.appendingPathComponent("Contents/MacOS"), withIntermediateDirectories: true)
            let plist: [String: Any] = ["CFBundleIdentifier": id, "CFBundleExecutable": "app", "CFBundlePackageType": "APPL"]
            try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
                .write(to: url.appendingPathComponent("Contents/Info.plist"))
            try "#!/bin/sh\necho \(content)\n".write(to: url.appendingPathComponent("Contents/MacOS/app"), atomically: true, encoding: .utf8)
            try manager.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.appendingPathComponent("Contents/MacOS/app").path)
            try run("/usr/bin/codesign", ["--force", "--sign", "-", url.path])
        }
        func makeImage(id: String, content: String) throws -> URL {
            let stage = folder.appendingPathComponent("stage-\(UUID().uuidString)")
            try makeApp(stage.appendingPathComponent(UpdateInstaller.appName), id: id, content: content)
            let image = folder.appendingPathComponent("\(UUID().uuidString).dmg")
            try run("/usr/bin/hdiutil", ["create", "-quiet", "-volname", "Test", "-srcfolder", stage.path, "-format", "UDZO", image.path])
            return image
        }
        let installed = folder.appendingPathComponent("Apps/\(UpdateInstaller.appName)")
        try makeApp(installed, id: "io.example.test", content: "old")
        let executable = installed.appendingPathComponent("Contents/MacOS/app")
        let good = try makeImage(id: "io.example.test", content: "new")
        let foreign = try makeImage(id: "io.example.other", content: "evil")
        func copy(_ image: URL) -> (URL) async throws -> URL {
            { _ in
                let temporary = folder.appendingPathComponent(UUID().uuidString + ".dmg")
                try manager.copyItem(at: image, to: temporary)
                return temporary
            }
        }
        func release(_ sha: String) -> ReleaseInfo {
            ReleaseInfo(version: AppVersion(9, 9, 9), tag: "mac-v9.9.9", url: URL(string: "https://github.com/a")!,
                        packageURL: URL(string: "https://github.com/p.dmg"), packageSHA256: sha)
        }
        func requirement(_ text: String) throws -> SecRequirement {
            var requirement: SecRequirement?
            guard SecRequirementCreateWithString(text as CFString, [], &requirement) == errSecSuccess, let requirement else {
                throw Failure(description: "bad requirement \(text)")
            }
            return requirement
        }
        let sameIdentity = try requirement(#"identifier "io.example.test""#)
        let wrong = try await expectError(UpdateFailure.self) {
            try await UpdateInstaller.install(release(String(repeating: "0", count: 64)), into: installed, bundleID: "io.example.test",
                                              requirement: sameIdentity, download: copy(good))
        }
        try check(wrong.message.contains("SHA-256"), "checksum failure reported")
        try check(try String(contentsOf: executable, encoding: .utf8).contains("old"), "app unchanged after checksum failure")
        _ = try await expectError(UpdateFailure.self) {
            try await UpdateInstaller.install(release(try UpdateInstaller.sha256(of: foreign)), into: installed, bundleID: "io.example.test",
                                              requirement: sameIdentity, download: copy(foreign))
        }
        try check(try String(contentsOf: executable, encoding: .utf8).contains("old"), "another app is never installed")
        let unsigned = try await expectError(UpdateFailure.self) {
            try await UpdateInstaller.install(release(try UpdateInstaller.sha256(of: good)), into: installed, bundleID: "io.example.test",
                                              requirement: try requirement(#"identifier "io.example.test" and anchor apple generic"#),
                                              download: copy(good))
        }
        try check(unsigned.message.contains("not signed like this one"), "a different signing identity is refused")
        _ = try await expectError(UpdateFailure.self) {
            // Ad hoc signatures pin the exact build, so an ad hoc copy never replaces itself with different code.
            try await UpdateInstaller.install(release(try UpdateInstaller.sha256(of: good)), into: installed, bundleID: "io.example.test",
                                              download: copy(good))
        }
        try check(try String(contentsOf: executable, encoding: .utf8).contains("old"), "app unchanged after signature failures")
        try await UpdateInstaller.install(release(try UpdateInstaller.sha256(of: good)), into: installed, bundleID: "io.example.test",
                                          requirement: sameIdentity, download: copy(good))
        try check(try String(contentsOf: executable, encoding: .utf8).contains("new"), "app replaced")
        let leftovers = try manager.contentsOfDirectory(atPath: installed.deletingLastPathComponent().path)
        try equal([UpdateInstaller.appName], leftovers)
        try check(UpdateInstaller.installableBundle(installed) == installed, "writable bundle is installable")
        try check(UpdateInstaller.installableBundle(folder) == nil, "only .app bundles update themselves")
    }),
    ("every UI string is translated with matching placeholders", {
        let placeholder = try NSRegularExpression(pattern: #"\{\d\}"#)
        func placeholders(_ text: String) -> [String] {
            placeholder.matches(in: text, range: NSRange(text.startIndex..., in: text)).map { (text as NSString).substring(with: $0.range) }.sorted()
        }
        for language in AppSettings.uiLanguages {
            let text = UiText(language)
            for key in UiText.keys {
                let value = text.t(key)
                try check(!value.isEmpty, "\(language): \(key)")
                try equal(placeholders(key), placeholders(value))
                if language != "en" { try check(value != key || key.allSatisfy({ !$0.isLetter }) || ["OK", "Inline Translator"].contains(key), "\(language) untranslated: \(key)") }
            }
        }
        guard let sourcesDirectory else { return }
        let call = try NSRegularExpression(pattern: #"\bt\("((?:[^"\\]|\\.)*)""#)
        let files = FileManager.default.enumerator(atPath: sourcesDirectory)?.compactMap { $0 as? String }.filter { $0.hasSuffix(".swift") } ?? []
        var used = Set<String>()
        for file in files {
            let source = try String(contentsOfFile: sourcesDirectory + "/" + file, encoding: .utf8)
            for match in call.matches(in: source, range: NSRange(source.startIndex..., in: source)) {
                used.insert((source as NSString).substring(with: match.range(at: 1)).replacingOccurrences(of: "\\n", with: "\n"))
            }
        }
        try check(used.count > 50, "source scan found the UI strings")
        let missing = used.subtracting(UiText.keys).sorted()
        try check(missing.isEmpty, "missing UI strings: \(missing)")
        let unused = Set(UiText.keys).subtracting(used).sorted()
        try check(unused.isEmpty, "unused UI strings: \(unused)")
    }),
    ("UI language follows the system when not chosen", {
        try equal("ko", UiText.resolve("", preferred: ["ko-KR"]))
        try equal("zh-CN", UiText.resolve("", preferred: ["zh-Hant-TW"]))
        try equal("en", UiText.resolve("", preferred: ["fr-FR"]))
        try equal("ja", UiText.resolve("ja", preferred: ["ko-KR"]))
        try equal("Inline Translator 가 {x}", UiText("ko").t("Settings").isEmpty ? "" : "Inline Translator 가 {x}")
    }),
]

var failures = 0
for (name, test) in tests {
    do {
        try await test()
        print("PASS \(name)")
    } catch {
        failures += 1
        FileHandle.standardError.write(Data("FAIL \(name): \(error)\n".utf8))
    }
}
try? FileManager.default.removeItem(at: testDirectory)
print("\(tests.count - failures)/\(tests.count) tests passed.")
exit(failures == 0 ? 0 : 1)
