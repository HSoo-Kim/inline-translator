import Combine
import Foundation

/// Interface strings. Keys are the English text; each entry holds the Korean, Simplified Chinese and Japanese text.
/// Placeholders are {0}, {1}, … and must appear in every translation.
final class UiText: ObservableObject {
    @Published private(set) var language = "en"

    init(_ language: String) { setLanguage(language) }

    func setLanguage(_ language: String) { self.language = UiText.resolve(language, preferred: Locale.preferredLanguages) }

    static func resolve(_ language: String, preferred: [String]) -> String {
        if AppSettings.uiLanguages.contains(language) { return language }
        let code = (preferred.first ?? "en").split(separator: "-").first.map(String.init)?.lowercased() ?? "en"
        switch code {
        case "ko": return "ko"
        case "ja": return "ja"
        case "zh": return "zh-CN"
        default: return "en"
        }
    }

    static var keys: [String] { Array(strings.keys) }

    func t(_ key: String, _ args: Any...) -> String {
        let format: String
        if let value = UiText.strings[key] {
            format = switch language {
            case "ko": value.korean
            case "zh-CN": value.chinese
            case "ja": value.japanese
            default: key
            }
        } else {
            assertionFailure("Missing UI string: \(key)")
            format = key
        }
        var text = format
        for (index, argument) in args.enumerated() {
            text = text.replacingOccurrences(of: "{\(index)}", with: String(describing: argument))
        }
        return text
    }

    private static let strings: [String: (korean: String, chinese: String, japanese: String)] = [
        "Inline Translator is running in the menu bar. Select text and press {0}.": ("Inline Translator가 메뉴 막대에서 실행 중입니다. 텍스트를 선택하고 {0}을(를) 누르세요.", "Inline Translator 正在菜单栏中运行。选择文本后按 {0}。", "Inline Translator はメニューバーで実行中です。テキストを選択して {0} を押してください。"),
        "Settings…": ("설정…", "设置…", "設定…"),
        "Settings": ("설정", "设置", "設定"),
        "Allow Accessibility…": ("손쉬운 사용 허용…", "允许辅助功能…", "アクセシビリティを許可…"),
        "Allow Accessibility": ("손쉬운 사용 허용", "允许辅助功能", "アクセシビリティを許可"),
        "Inline Translator needs Accessibility permission to copy the selected text and paste the translation. Turn on Inline Translator in System Settings > Privacy & Security > Accessibility.": ("선택한 텍스트를 복사하고 번역문을 붙여 넣으려면 손쉬운 사용 권한이 필요합니다. 시스템 설정 > 개인정보 보호 및 보안 > 손쉬운 사용에서 Inline Translator를 켜세요.", "Inline Translator 需要辅助功能权限才能复制所选文本并粘贴译文。请在系统设置 > 隐私与安全性 > 辅助功能中开启 Inline Translator。", "選択したテキストをコピーして訳文をペーストするには、Inline Translator にアクセシビリティの権限が必要です。システム設定 > プライバシーとセキュリティ > アクセシビリティで Inline Translator をオンにしてください。"),
        "Allow Inline Translator in Privacy & Security > Accessibility so it can copy and paste the selected text. Click here to open it.": ("선택한 텍스트를 복사하고 붙여 넣을 수 있도록 개인정보 보호 및 보안 > 손쉬운 사용에서 Inline Translator를 허용하세요. 여기를 누르면 해당 설정이 열립니다.", "请在隐私与安全性 > 辅助功能中允许 Inline Translator，以便复制和粘贴所选文本。点击此处打开设置。", "選択したテキストをコピーしてペーストできるよう、プライバシーとセキュリティ > アクセシビリティで Inline Translator を許可してください。ここをクリックして設定を開きます。"),
        "Open System Settings": ("시스템 설정 열기", "打开系统设置", "システム設定を開く"),
        "Translation": ("번역", "翻译", "翻訳"),
        "Engines": ("엔진", "引擎", "エンジン"),
        "Accounts": ("계정", "账户", "アカウント"),
        "Provider": ("엔진", "提供方", "プロバイダー"),
        "Appearance": ("모양", "外观", "表示"),
        "Hotkey": ("단축키", "快捷键", "ショートカット"),
        "Target language": ("번역 언어", "目标语言", "翻訳先の言語"),
        "Writing hotkey {0}": ("쓰기 단축키 {0}", "写作快捷键 {0}", "入力用ショートカット {0}"),
        "Your language and the hotkey language translate in both directions.": ("내 언어와 단축키의 번역 언어 사이를 양방향으로 번역합니다.", "在“我的语言”和快捷键的目标语言之间双向翻译。", "「自分の言語」とショートカットの言語の間で双方向に翻訳します。"),
        "Text in My language goes to the selected language; other text comes to My language. The original stays unchanged.": ("내 언어로 쓴 글은 선택한 언어로, 다른 언어의 글은 내 언어로 번역합니다. 원문은 바뀌지 않습니다.", "我的语言写的文本译成所选语言，其他语言译成我的语言。原文保持不变。", "自分の言語の文章は選択した言語に、その他の文章は自分の言語に翻訳します。原文は変わりません。"),
        "Display language changes after Save. Reopen Settings to see it.": ("저장하면 화면 언어가 바뀝니다. 설정을 다시 열면 적용됩니다.", "保存后更改界面语言。重新打开设置即可查看。", "保存後に表示言語が変わります。設定を開き直すと反映されます。"),
        "Choose a hotkey.": ("단축키를 지정하세요.", "请选择快捷键。", "ショートカットを指定してください。"),
        "Use ⌃, ⌥ or ⌘ with a letter, number or function key.": ("⌃, ⌥ 또는 ⌘과 문자·숫자·F키를 함께 누르세요.", "请将 ⌃、⌥ 或 ⌘ 与字母、数字或功能键组合使用。", "⌃、⌥ または ⌘ と文字・数字・F キーを組み合わせてください。"),
        "This hotkey is also used by another translation shortcut.": ("다른 번역 단축키에서도 쓰고 있습니다.", "此组合键也用于其他翻译快捷键。", "ほかの翻訳ショートカットと重複しています。"),
        "This hotkey is already used by another app. Choose another.": ("다른 앱에서 쓰는 단축키입니다. 다른 조합을 지정하세요.", "此快捷键已被其他应用占用。请选择其他组合键。", "別のアプリが使用中です。ほかのショートカットを指定してください。"),
        "Test succeeded · {0} · {1}s\n{2}": ("테스트 성공 · {0} · {1}초\n{2}", "测试成功 · {0} · {1}秒\n{2}", "テスト成功 · {0} · {1}秒\n{2}"),
        "Test failed · {0}": ("테스트 실패 · {0}", "测试失败 · {0}", "テスト失敗 · {0}"),
        "Pause hotkeys": ("단축키 일시 중지", "暂停快捷键", "ショートカットを一時停止"),
        "Paused": ("일시 중지됨", "已暂停", "一時停止中"),
        "Quit Inline Translator": ("Inline Translator 종료", "退出 Inline Translator", "Inline Translator を終了"),
        "Update to {0}": ("{0}(으)로 업데이트", "更新到 {0}", "{0} に更新"),
        "Update": ("업데이트", "更新", "更新"),
        "Check for updates": ("지금 업데이트 확인", "立即检查更新", "今すぐ更新を確認"),
        "Checking for updates…": ("업데이트 확인 중…", "正在检查更新…", "更新を確認中…"),
        "Update available: {0}.": ("새 버전 {0}을 설치할 수 있습니다.", "新版本 {0} 可供安装。", "新しいバージョン {0} をインストールできます。"),
        "You are using the latest version ({0}).": ("최신 버전입니다({0}).", "当前已是最新版本（{0}）。", "最新バージョンです（{0}）。"),
        "Update check failed: {0}": ("업데이트 확인 실패: {0}", "检查更新失败：{0}", "更新の確認に失敗：{0}"),
        "Languages and tone": ("언어와 말투", "语言与语气", "言語とトーン"),
        "{0} is already used by another app. Choose another hotkey in Settings.": ("{0}을(를) 다른 앱이 이미 쓰고 있습니다. 설정에서 다른 단축키를 고르세요.", "{0} 已被其他应用占用。请在设置中选择其他快捷键。", "{0} は別のアプリが使用しています。設定で別のショートカットを選んでください。"),
        "Select the text to translate first, then press the hotkey.": ("번역할 텍스트를 먼저 선택한 뒤 단축키를 누르세요.", "请先选择要翻译的文本，再按快捷键。", "翻訳するテキストを選択してからショートカットを押してください。"),
        "Translating to {0}… · Esc to cancel": ("{0} 번역 중… · Esc로 취소", "正在翻译为{0}… · 按 Esc 取消", "{0}に翻訳中… · Esc でキャンセル"),
        "Translating to {0} with context… · Esc to cancel": ("{0} 번역 중(앞뒤 문맥 반영)… · Esc로 취소", "正在结合上下文翻译为{0}… · 按 Esc 取消", "前後の文脈を考慮して{0}に翻訳中… · Esc でキャンセル"),
        "Input changed · translation shown without replacing text": ("입력 내용이 바뀌어 글을 바꾸지 않고 번역문만 표시합니다", "输入内容已更改 · 仅显示译文，不替换文本", "入力内容が変わりました · テキストを置き換えずに訳文を表示します"),
        "Translation · press any key or click to close": ("번역 · 아무 키나 누르거나 클릭하면 닫힙니다", "译文 · 按任意键或点击即可关闭", "翻訳 · 何かキーを押すかクリックで閉じます"),
        "Translation failed: {0}": ("번역 실패: {0}", "翻译失败：{0}", "翻訳に失敗しました: {0}"),
        "Inline Translator {0} is available. Click the menu bar icon and choose Update.": ("Inline Translator {0}이(가) 나왔습니다. 메뉴 막대 아이콘을 클릭해 업데이트를 누르세요.", "Inline Translator {0} 已发布。点击菜单栏图标并选择“更新”。", "Inline Translator {0} が公開されました。メニューバーのアイコンをクリックして「更新」を選んでください。"),
        "Updating to {0}…": ("{0}(으)로 업데이트 중…", "正在更新到 {0}…", "{0} に更新中…"),
        "Automatic update failed: {0}": ("자동 업데이트 실패: {0}", "自动更新失败：{0}", "自動更新に失敗しました: {0}"),
        "Primary": ("기본", "主要", "メイン"),
        "If it fails": ("실패하면", "失败时", "失敗したら"),
        "Uses the Claude Code or Codex you are already signed in to on this Mac. No API key needed.": ("이 Mac에 로그인된 Claude Code나 Codex를 그대로 사용합니다. API 키가 필요 없습니다.", "直接使用此 Mac 上已登录的 Claude Code 或 Codex，无需 API 密钥。", "この Mac でサインイン済みの Claude Code または Codex をそのまま使います。API キーは不要です。"),
        "Hotkeys and target languages": ("단축키와 번역 언어", "快捷键与目标语言", "ショートカットと翻訳先の言語"),
        "Read-only translation hotkey": ("읽기 전용 번역 단축키", "只读翻译快捷键", "読み取り専用の翻訳ショートカット"),
        "Select text and press a hotkey: it is replaced with the translation in place.": ("텍스트를 선택하고 단축키를 누르면 그 자리에서 번역문으로 바뀝니다.", "选择文本并按快捷键，文本会被原地替换为译文。", "テキストを選択してショートカットを押すと、その場で訳文に置き換わります。"),
        "Tone": ("말투", "语气", "トーン"),
        "Match the original": ("원문 그대로", "保持原文语气", "原文に合わせる"),
        "Casual": ("편하게", "随意", "カジュアル"),
        "Polite": ("정중하게", "礼貌", "丁寧"),
        "Consider the surrounding text": ("앞뒤 문맥 반영", "参考前后文", "前後の文脈を考慮"),
        "When the app exposes the text around a selection, up to 1,000 characters before and 500 after go to the engine with it, so terms and references are translated in context. Only the selection is translated.":
            ("앱이 선택한 부분의 앞뒤 글을 알려 주면 앞 1,000자·뒤 500자까지 함께 엔진에 보내 용어와 지칭을 문맥에 맞게 번역합니다. 번역되는 것은 선택한 부분뿐입니다.",
             "如果应用提供所选内容前后的文本，会将前面最多 1,000 个字符和后面最多 500 个字符一并发送给引擎，使术语和指代符合上下文。只翻译所选部分。",
             "アプリが選択範囲の前後の文章を提供する場合、前の最大 1,000 文字と後の最大 500 文字を一緒にエンジンへ送り、用語や指示語を文脈に合わせて翻訳します。翻訳されるのは選択した部分だけです。"),
        "Signed in": ("로그인됨", "已登录", "サインイン済み"),
        "Sign-in needed": ("로그인 필요", "需要登录", "サインインが必要"),
        "Not installed": ("설치 안 됨", "未安装", "未インストール"),
        "Status unknown": ("상태 확인 안 됨", "状态未知", "状態不明"),
        "Checking…": ("확인 중…", "正在检查…", "確認中…"),
        "How to install": ("설치 방법", "安装方法", "インストール方法"),
        "Sign in": ("로그인", "登录", "サインイン"),
        "{0} needs you to sign in. Click here to sign in.": ("{0} 로그인이 필요합니다. 여기를 누르면 로그인 창이 열립니다.", "{0} 需要登录。点击此处登录。", "{0} にサインインが必要です。ここをクリックしてサインインします。"),
        "{0} needs you to sign in again, so {1} translated this. Click here to sign in to {0}.": ("{0} 로그인이 필요해서 이번엔 {1}(으)로 번역했습니다. 여기를 누르면 {0} 로그인 창이 열립니다.", "{0} 需要重新登录，本次已由 {1} 翻译。点击此处登录 {0}。", "{0} の再サインインが必要なため、今回は {1} で翻訳しました。ここをクリックして {0} にサインインします。"),
        "General": ("일반", "常规", "全般"),
        "Display language": ("화면 언어", "界面语言", "表示言語"),
        "System default": ("시스템 기본값", "跟随系统", "システムの既定"),
        "Open at login": ("로그인 시 열기", "登录时打开", "ログイン時に開く"),
        "Check for new versions": ("새 버전 확인", "检查新版本", "新しいバージョンを確認"),
        "Install automatically when Update is clicked": ("업데이트를 누르면 자동으로 설치", "点击“更新”时自动安装", "「更新」をクリックしたら自動でインストール"),
        "Save": ("저장", "保存", "保存"),
        "Cancel": ("취소", "取消", "キャンセル"),
        "not found": ("찾을 수 없음", "未找到", "見つかりません"),
        "Could not save settings: {0}": ("설정을 저장하지 못했습니다: {0}", "无法保存设置：{0}", "設定を保存できませんでした: {0}"),
        "Testing {0}…": ("{0} 테스트 중…", "正在测试 {0}…", "{0} をテスト中…"),
        "None": ("사용 안 함", "不使用", "使わない"),
        "Test": ("테스트", "测试", "テスト"),
        "Press a key combination": ("단축키를 누르세요", "请按组合键", "キーの組み合わせを押してください"),
        "Apps that translate the whole input box": ("입력창 전체를 번역할 앱", "翻译整个输入框的应用", "入力欄全体を翻訳するアプリ"),
        "In these apps, pressing the hotkey with nothing selected translates the whole message. Drag-select first to translate only part of it.": ("이 앱에서는 아무것도 선택하지 않고 단축키를 누르면 입력창 전체를 번역합니다. 일부만 번역하려면 드래그로 선택한 뒤 누르세요.", "在这些应用中，未选中文本时按快捷键会翻译整条消息。只想翻译一部分时，先拖动选中再按。", "これらのアプリでは、何も選択せずにショートカットを押すとメッセージ全体を翻訳します。一部だけ翻訳するにはドラッグで選択してから押してください。"),
        "+ Add apps": ("+ 앱 추가", "+ 添加应用", "+ アプリを追加"),
        "Remove {0}": ("{0} 제거", "移除 {0}", "{0} を削除"),
        "Enter a model name…": ("직접 입력…", "手动输入…", "直接入力…"),
        "Model": ("모델", "模型", "モデル"),
        "Type the exact model name, for example claude-opus-5-5.": ("정확한 모델 이름을 입력하세요. 예: claude-opus-5-5", "请输入准确的模型名称，例如 claude-opus-5-5。", "正確なモデル名を入力してください。例: claude-opus-5-5"),
        "OK": ("확인", "确定", "OK"),
        "Choose apps": ("앱 고르기", "选择应用", "アプリを選ぶ"),
        "Pick the chat apps where pressing the hotkey without selecting should translate the whole message.": ("선택 없이 단축키를 눌렀을 때 입력창 전체를 번역할 메신저 앱을 고르세요.", "选择在未选中文本时按快捷键即翻译整条消息的聊天应用。", "選択せずにショートカットを押したときにメッセージ全体を翻訳するチャットアプリを選んでください。"),
        "Search apps": ("앱 검색", "搜索应用", "アプリを検索"),
        "Running": ("실행 중", "运行中", "実行中"),
        "Done": ("완료", "完成", "完了"),
        "Chat app": ("메신저", "聊天应用", "チャットアプリ"),
        "Could not read the input box in {0}. Drag-select the text, then press the hotkey.": ("{0}에서 입력창 내용을 가져오지 못했습니다. 번역할 부분을 드래그로 선택한 뒤 단축키를 누르세요.", "无法读取 {0} 的输入框内容。请拖动选中要翻译的文本后再按快捷键。", "{0} の入力欄を読み取れませんでした。翻訳する部分をドラッグで選択してからショートカットを押してください。"),
        "That looks like a whole document, not a chat message, so nothing was translated. Drag-select the part to translate, or remove {0} from the whole-input apps in Settings.": ("채팅 메시지가 아니라 문서 전체가 잡힌 것 같아 번역하지 않았습니다. 번역할 부분을 드래그로 선택하거나, 설정의 입력창 전체 번역 앱에서 {0}을(를) 빼세요.", "选中的内容看起来是整篇文档而不是聊天消息，因此未翻译。请拖动选中要翻译的部分，或在设置中将 {0} 从整框翻译应用中移除。", "チャットのメッセージではなく文書全体が選択されたようなので翻訳しませんでした。翻訳する部分をドラッグで選択するか、設定の入力欄全体を翻訳するアプリから {0} を外してください。"),
        "Read-only translation bubble": ("읽기 전용 번역 말풍선", "只读文本翻译气泡", "読み取り専用の翻訳吹き出し"),
        "White background, black text": ("흰 배경 + 검정 글자", "白底黑字", "白い背景 + 黒い文字"),
        "Black background, white text": ("검정 배경 + 흰 글자", "黑底白字", "黒い背景 + 白い文字"),
        "Bubble position": ("말풍선 위치", "气泡位置", "吹き出しの位置"),
        "Follows the mouse": ("마우스를 따라다님", "跟随鼠标", "マウスに追従"),
        "Right above or below the selected text": ("선택한 글 바로 위·아래", "选中文本的正上方或正下方", "選択したテキストのすぐ上・下"),
        "My language": ("내 언어", "我的语言", "自分の言語"),
    ]
}
