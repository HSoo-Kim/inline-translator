import Foundation

/// Text right before and after the selection in the same document, sent to the engine for reference only.
struct TranslationContext: Equatable {
    let before: String
    let after: String
}

enum ContextWindow {
    static let maxBefore = 1000
    static let maxAfter = 500

    /// Context around `selection` (a UTF-16 range, as Accessibility reports it) inside `text`, or nil when the
    /// range does not hold the copied text, which means the app reported another element than the one copied from.
    static func around(text: String, selection: NSRange, selected: String) -> TranslationContext? {
        let full = text as NSString
        guard selection.location != NSNotFound, NSMaxRange(selection) <= full.length,
              same(full.substring(with: selection), selected) else { return nil }
        let start = max(0, selection.location - maxBefore - 1)
        let end = min(full.length, NSMaxRange(selection) + maxAfter + 1)
        return clip(before: full.substring(with: NSRange(location: start, length: selection.location - start)),
                    after: full.substring(with: NSRange(location: NSMaxRange(selection), length: end - NSMaxRange(selection))))
    }

    /// Keeps the end of `before` and the start of `after` within the limits, cut at a line break or space
    /// so no half word is sent; nil when both are empty.
    static func clip(before: String, after: String) -> TranslationContext? {
        var head = lines(before) as NSString, tail = lines(after) as NSString
        let space = CharacterSet.whitespacesAndNewlines
        func isSpace(_ text: NSString, _ index: Int) -> Bool {
            text.substring(with: text.rangeOfComposedCharacterSequence(at: index)).unicodeScalars.allSatisfy(space.contains)
        }
        if head.length > maxBefore {
            var cut = head.length - maxBefore
            let character = head.rangeOfComposedCharacterSequence(at: cut)
            if character.location < cut { cut = NSMaxRange(character) }
            let midWord = !isSpace(head, cut - 1)
            head = head.substring(from: cut) as NSString
            if midWord { head = head.substring(from: cutStart(head)) as NSString }
        }
        if tail.length > maxAfter {
            var cut = maxAfter
            let character = tail.rangeOfComposedCharacterSequence(at: cut - 1)
            if NSMaxRange(character) > cut { cut = character.location }
            let midWord = !isSpace(tail, cut)
            tail = tail.substring(to: cut) as NSString
            if midWord { tail = tail.substring(to: cutEnd(tail)) as NSString }
        }
        let context = TranslationContext(before: (head as String).trimmingCharacters(in: space),
                                         after: (tail as String).trimmingCharacters(in: space))
        return context.before.isEmpty && context.after.isEmpty ? nil : context
    }

    private static func lines(_ text: String) -> String {
        text.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
    }

    static func same(_ first: String, _ second: String) -> Bool {
        func normalize(_ text: String) -> String {
            text.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
                .trimmingCharacters(in: .whitespacesAndNewlines)
        }
        let a = normalize(first)
        return !a.isEmpty && a == normalize(second)
    }

    private static func cutStart(_ text: NSString) -> Int {
        let newline = text.range(of: "\n")
        if newline.location != NSNotFound && newline.location < 200 { return NSMaxRange(newline) }
        let space = text.rangeOfCharacter(from: .whitespaces)
        return space.location != NSNotFound && space.location < 40 ? NSMaxRange(space) : 0
    }

    private static func cutEnd(_ text: NSString) -> Int {
        let newline = text.range(of: "\n", options: .backwards)
        if newline.location != NSNotFound && text.length - newline.location < 200 { return newline.location }
        let space = text.rangeOfCharacter(from: .whitespaces, options: .backwards)
        return space.location != NSNotFound && text.length - space.location < 40 ? space.location : text.length
    }
}
