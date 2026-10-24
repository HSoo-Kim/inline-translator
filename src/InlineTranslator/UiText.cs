using System.Globalization;

namespace InlineTranslator;

public sealed class UiText
{
    public string Language { get; private set; } = "en";

    public UiText(string language) => SetLanguage(language);

    public void SetLanguage(string language) => Language = Resolve(language, CultureInfo.CurrentUICulture);

    public static string Resolve(string language, CultureInfo culture)
    {
        if (AppSettings.UiLanguages.Contains(language)) return language;
        return culture.TwoLetterISOLanguageName switch
        {
            "ko" => "ko",
            "ja" => "ja",
            "zh" => "zh-CN",
            _ => "en",
        };
    }

    public static IEnumerable<string> Keys => Strings.Keys;

    public string T(string key, params object?[] args)
    {
        var value = Strings[key];
        string format = Language switch { "ko" => value.Korean, "zh-CN" => value.Chinese, "ja" => value.Japanese, _ => key };
        return args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
    }

    private static readonly Dictionary<string, (string Korean, string Chinese, string Japanese)> Strings = new()
    {
        ["Inline Translator is running in the notification area. Select text and press {0}."] =
            ("Inline Translator가 알림 영역에서 실행 중입니다. 텍스트를 선택하고 {0}을(를) 누르세요.",
             "Inline Translator 正在通知区域中运行。选择文本后按 {0}。",
             "Inline Translator は通知領域で実行中です。テキストを選択して {0} を押してください。"),
        ["Settings…"] = ("설정…", "设置…", "設定…"),
        ["Settings"] = ("설정", "设置", "設定"),
        ["Translation"] = ("번역", "翻译", "翻訳"),
        ["Engines"] = ("엔진", "引擎", "エンジン"),
        ["Accounts"] = ("계정", "账户", "アカウント"),
        ["Provider"] = ("엔진", "提供方", "プロバイダー"),
        ["Appearance"] = ("모양", "外观", "表示"),
        ["Hotkey"] = ("단축키", "快捷键", "ショートカット"),
        ["Target language"] = ("번역 언어", "目标语言", "翻訳先の言語"),
        ["Writing hotkey {0}"] = ("쓰기 단축키 {0}", "写作快捷键 {0}", "入力用ショートカット {0}"),
        ["Your language and the hotkey language translate in both directions."] =
            ("내 언어와 단축키의 번역 언어 사이를 양방향으로 번역합니다.",
             "在“我的语言”和快捷键的目标语言之间双向翻译。",
             "「自分の言語」とショートカットの言語の間で双方向に翻訳します。"),
        ["Text in My language goes to the selected language; other text comes to My language. The original stays unchanged."] =
            ("내 언어로 쓴 글은 선택한 언어로, 다른 언어의 글은 내 언어로 번역합니다. 원문은 바뀌지 않습니다.",
             "我的语言写的文本译成所选语言，其他语言译成我的语言。原文保持不变。",
             "自分の言語の文章は選択した言語に、その他の文章は自分の言語に翻訳します。原文は変わりません。"),
        ["Display language changes after Save. Reopen Settings to see it."] =
            ("저장하면 화면 언어가 바뀝니다. 설정을 다시 열면 적용됩니다.",
             "保存后更改界面语言。重新打开设置即可查看。",
             "保存後に表示言語が変わります。設定を開き直すと反映されます。"),
        ["Choose a hotkey."] = ("단축키를 지정하세요.", "请选择快捷键。", "ショートカットを指定してください。"),
        ["Use Ctrl, Alt or Win with a letter, number or function key."] =
            ("Ctrl, Alt 또는 Win과 문자·숫자·F키를 함께 누르세요.",
             "请将 Ctrl、Alt 或 Win 与字母、数字或功能键组合使用。",
             "Ctrl、Alt または Win と文字・数字・F キーを組み合わせてください。"),
        ["This hotkey is also used by another translation shortcut."] =
            ("다른 번역 단축키에서도 쓰고 있습니다.",
             "此组合键也用于其他翻译快捷键。",
             "ほかの翻訳ショートカットと重複しています。"),
        ["This hotkey is already used by another app. Choose another."] =
            ("다른 앱에서 쓰는 단축키입니다. 다른 조합을 지정하세요.",
             "此快捷键已被其他应用占用。请选择其他组合键。",
             "別のアプリが使用中です。ほかのショートカットを指定してください。"),
        ["Test succeeded · {0} · {1:0.0}s\n{2}"] =
            ("테스트 성공 · {0} · {1:0.0}초\n{2}",
             "测试成功 · {0} · {1:0.0}秒\n{2}",
             "テスト成功 · {0} · {1:0.0}秒\n{2}"),
        ["Test failed · {0}"] = ("테스트 실패 · {0}", "测试失败 · {0}", "テスト失敗 · {0}"),
        ["Pause hotkeys"] = ("단축키 일시 중지", "暂停快捷键", "ショートカットを一時停止"),
        ["Paused"] = ("일시 중지됨", "已暂停", "一時停止中"),
        ["Exit"] = ("종료", "退出", "終了"),
        ["Update to {0}"] = ("{0}(으)로 업데이트", "更新到 {0}", "{0} に更新"),
        ["Update"] = ("업데이트", "更新", "更新"),
        ["Check for updates"] = ("지금 업데이트 확인", "立即检查更新", "今すぐ更新を確認"),
        ["Checking for updates…"] = ("업데이트 확인 중…", "正在检查更新…", "更新を確認中…"),
        ["Update available: {0}."] = ("새 버전 {0}을 설치할 수 있습니다.", "新版本 {0} 可供安装。", "新しいバージョン {0} をインストールできます。"),
        ["You are using the latest version ({0})."] = ("최신 버전입니다({0}).", "当前已是最新版本（{0}）。", "最新バージョンです（{0}）。"),
        ["Update check failed: {0}"] = ("업데이트 확인 실패: {0}", "检查更新失败：{0}", "更新の確認に失敗：{0}"),
        ["Languages and tone"] = ("언어와 말투", "语言与语气", "言語とトーン"),
        ["{0} is already used by another program. Choose another hotkey in Settings."] =
            ("{0}을(를) 다른 프로그램이 이미 쓰고 있습니다. 설정에서 다른 단축키를 고르세요.",
             "{0} 已被其他程序占用。请在设置中选择其他快捷键。",
             "{0} は別のプログラムが使用しています。設定で別のショートカットを選んでください。"),
        ["Select the text to translate first, then press the hotkey."] =
            ("번역할 텍스트를 먼저 선택한 뒤 단축키를 누르세요.", "请先选择要翻译的文本，再按快捷键。", "翻訳するテキストを選択してからショートカットを押してください。"),
        ["Translating to {0}…"] = ("{0} 번역 중…", "正在翻译为{0}…", "{0}に翻訳中…"),
        ["Translating to {0}… · Esc to cancel"] = ("{0} 번역 중… · Esc로 취소", "正在翻译为{0}… · 按 Esc 取消", "{0}に翻訳中… · Esc でキャンセル"),
        ["Input changed · translation shown without replacing text"] =
            ("입력 내용이 바뀌어 글을 바꾸지 않고 번역문만 표시합니다", "输入内容已更改 · 仅显示译文，不替换文本", "入力内容が変わりました · テキストを置き換えずに訳文を表示します"),
        ["Translation · press any key or click to close"] =
            ("번역 · 아무 키나 누르거나 클릭하면 닫힙니다", "译文 · 按任意键或点击即可关闭", "翻訳 · 何かキーを押すかクリックで閉じます"),
        ["You switched windows, so the translation was copied instead. Paste it with Ctrl+V."] =
            ("창이 바뀌어서 번역 결과를 클립보드에 복사해 두었습니다. Ctrl+V로 붙여 넣으세요.",
             "窗口已切换，翻译结果已复制到剪贴板。请按 Ctrl+V 粘贴。",
             "ウィンドウが切り替わったため、翻訳結果をクリップボードにコピーしました。Ctrl+V で貼り付けてください。"),
        ["Translation failed: {0}"] = ("번역 실패: {0}", "翻译失败：{0}", "翻訳に失敗しました: {0}"),
        ["Inline Translator {0} is available. Right-click the tray icon and choose Update."] =
            ("Inline Translator {0}이(가) 나왔습니다. 알림 영역 아이콘을 우클릭해 업데이트를 누르세요.",
             "Inline Translator {0} 已发布。右键单击托盘图标并选择“更新”。",
             "Inline Translator {0} が公開されました。通知領域のアイコンを右クリックして「更新」を選んでください。"),
        ["Updating to {0}…"] = ("{0}(으)로 업데이트 중…", "正在更新到 {0}…", "{0} に更新中…"),
        ["Automatic update failed: {0}"] = ("자동 업데이트 실패: {0}", "自动更新失败：{0}", "自動更新に失敗しました: {0}"),
        ["Primary"] = ("기본", "主要", "メイン"),
        ["If it fails"] = ("실패하면", "失败时", "失敗したら"),
        ["Translation engine"] = ("번역 엔진", "翻译引擎", "翻訳エンジン"),
        ["Uses the Claude Code or Codex you are already signed in to on this PC. No API key needed."] =
            ("이 PC에 로그인된 Claude Code나 Codex를 그대로 사용합니다. API 키가 필요 없습니다.",
             "直接使用此电脑上已登录的 Claude Code 或 Codex，无需 API 密钥。",
             "この PC でサインイン済みの Claude Code または Codex をそのまま使います。API キーは不要です。"),
        ["Hotkeys and target languages"] = ("단축키와 번역 언어", "快捷键与目标语言", "ショートカットと翻訳先の言語"),
        ["Read-only translation hotkey"] = ("읽기 전용 번역 단축키", "只读翻译快捷键", "読み取り専用の翻訳ショートカット"),
        ["Select text first. This hotkey always translates into My language and only shows a bubble, without pasting or replacing text."] =
            ("선택한 글을 항상 내 언어로 번역합니다.\n원문은 바꾸지 않고 말풍선으로 표시합니다.",
             "选中文字始终译成“我的语言”。\n仅显示气泡，不粘贴或修改原文。",
             "選択した文章を常に「自分の言語」に翻訳。\n原文を変えず、吹き出しで表示します。"),
        ["Select text and press a hotkey: it is replaced with the translation in place."] =
            ("텍스트를 선택하고 단축키를 누르면 그 자리에서 번역문으로 바뀝니다.",
             "选择文本并按快捷键，文本会被原地替换为译文。",
             "テキストを選択してショートカットを押すと、その場で訳文に置き換わります。"),
        ["Tone"] = ("말투", "语气", "トーン"),
        ["Match the original"] = ("원문 그대로", "保持原文语气", "原文に合わせる"),
        ["Casual"] = ("편하게", "随意", "カジュアル"),
        ["Polite"] = ("정중하게", "礼貌", "丁寧"),
        ["Signed in"] = ("로그인됨", "已登录", "サインイン済み"),
        ["Sign-in needed"] = ("로그인 필요", "需要登录", "サインインが必要"),
        ["Not installed"] = ("설치 안 됨", "未安装", "未インストール"),
        ["Status unknown"] = ("상태 확인 안 됨", "状态未知", "状態不明"),
        ["Checking…"] = ("확인 중…", "正在检查…", "確認中…"),
        ["How to install"] = ("설치 방법", "安装方法", "インストール方法"),
        ["Sign in"] = ("로그인", "登录", "サインイン"),
        ["{0} needs you to sign in. Click here to sign in."] =
            ("{0} 로그인이 필요합니다. 여기를 누르면 로그인 창이 열립니다.",
             "{0} 需要登录。点击此处登录。",
             "{0} にサインインが必要です。ここをクリックしてサインインします。"),
        ["{0} needs you to sign in again, so {1} translated this. Click here to sign in to {0}."] =
            ("{0} 로그인이 필요해서 이번엔 {1}(으)로 번역했습니다. 여기를 누르면 {0} 로그인 창이 열립니다.",
             "{0} 需要重新登录，本次已由 {1} 翻译。点击此处登录 {0}。",
             "{0} の再サインインが必要なため、今回は {1} で翻訳しました。ここをクリックして {0} にサインインします。"),
        ["General"] = ("일반", "常规", "全般"),
        ["Display language"] = ("화면 언어", "界面语言", "表示言語"),
        ["System default"] = ("시스템 기본값", "跟随系统", "システムの既定"),
        ["Start with Windows"] = ("Windows 시작 시 실행", "开机时启动", "Windows の起動時に実行"),
        ["Check for new versions"] = ("새 버전 확인", "检查新版本", "新しいバージョンを確認"),
        ["Install automatically when Update is clicked"] =
            ("업데이트를 누르면 자동으로 설치", "点击“更新”时自动安装", "「更新」をクリックしたら自動でインストール"),
        ["Save"] = ("저장", "保存", "保存"),
        ["Cancel"] = ("취소", "取消", "キャンセル"),
        ["not found"] = ("찾을 수 없음", "未找到", "見つかりません"),
        ["The same hotkey is used twice: {0}"] = ("같은 단축키가 두 번 쓰였습니다: {0}", "同一快捷键被使用了两次：{0}", "同じショートカットが重複しています: {0}"),
        ["Could not save settings: {0}"] = ("설정을 저장하지 못했습니다: {0}", "无法保存设置：{0}", "設定を保存できませんでした: {0}"),
        ["Testing {0}…"] = ("{0} 테스트 중…", "正在测试 {0}…", "{0} をテスト中…"),
        ["None"] = ("사용 안 함", "不使用", "使わない"),
        ["Test"] = ("테스트", "测试", "テスト"),
        ["Press a key combination"] = ("단축키를 누르세요", "请按组合键", "キーの組み合わせを押してください"),
        ["Ctrl or Alt + a letter, digit or F-key"] = ("Ctrl 또는 Alt + 문자·숫자·F키", "Ctrl 或 Alt + 字母、数字或 F 键", "Ctrl または Alt + 文字・数字・F キー"),
        ["Apps that translate the whole input box"] = ("입력창 전체를 번역할 앱", "翻译整个输入框的应用", "入力欄全体を翻訳するアプリ"),
        ["Select text, press the hotkey, done."] = ("선택하고 단축키 한 번이면 끝.", "选中文本，按下快捷键，完成。", "選択してショートカットを押すだけ。"),
        ["In these apps, pressing the hotkey with nothing selected translates the whole message. Drag-select first to translate only part of it."] = ("이 앱에서는 아무것도 선택하지 않고 단축키를 누르면 입력창 전체를 번역합니다. 일부만 번역하려면 드래그로 선택한 뒤 누르세요.", "在这些应用中，未选中文本时按快捷键会翻译整条消息。只想翻译一部分时，先拖动选中再按。", "これらのアプリでは、何も選択せずにショートカットを押すとメッセージ全体を翻訳します。一部だけ翻訳するにはドラッグで選択してから押してください。"),
        ["+ Add apps"] = ("+ 앱 추가", "+ 添加应用", "+ アプリを追加"),
        ["Enter a model name…"] = ("직접 입력…", "手动输入…", "直接入力…"),
        ["Model"] = ("모델", "模型", "モデル"),
        ["Type the exact model name, for example claude-opus-5-5."] = ("정확한 모델 이름을 입력하세요. 예: claude-opus-5-5", "请输入准确的模型名称，例如 claude-opus-5-5。", "正確なモデル名を入力してください。例: claude-opus-5-5"),
        ["OK"] = ("확인", "确定", "OK"),
        ["Choose apps"] = ("앱 고르기", "选择应用", "アプリを選ぶ"),
        ["Pick the chat apps where pressing the hotkey without selecting should translate the whole message."] = ("선택 없이 단축키를 눌렀을 때 입력창 전체를 번역할 메신저 앱을 고르세요.", "选择在未选中文本时按快捷键即翻译整条消息的聊天应用。", "選択せずにショートカットを押したときにメッセージ全体を翻訳するチャットアプリを選んでください。"),
        ["Search apps"] = ("앱 검색", "搜索应用", "アプリを検索"),
        ["Running"] = ("실행 중", "运行中", "実行中"),
        ["Done"] = ("완료", "完成", "完了"),
        ["Chat app"] = ("메신저", "聊天应用", "チャットアプリ"),
        ["Could not read the input box in {0}. Drag-select the text, then press the hotkey."] = ("{0}에서 입력창 내용을 가져오지 못했습니다. 번역할 부분을 드래그로 선택한 뒤 단축키를 누르세요.", "无法读取 {0} 的输入框内容。请拖动选中要翻译的文本后再按快捷键。", "{0} の入力欄を読み取れませんでした。翻訳する部分をドラッグで選択してからショートカットを押してください。"),
        ["That looks like a whole document, not a chat message, so nothing was translated. Drag-select the part to translate, or remove {0} from the whole-input apps in Settings."] = ("채팅 메시지가 아니라 문서 전체가 잡힌 것 같아 번역하지 않았습니다. 번역할 부분을 드래그로 선택하거나, 설정의 입력창 전체 번역 앱에서 {0}을(를) 빼세요.", "选中的内容看起来是整篇文档而不是聊天消息，因此未翻译。请拖动选中要翻译的部分，或在设置中将 {0} 从整框翻译应用中移除。", "チャットのメッセージではなく文書全体が選択されたようなので翻訳しませんでした。翻訳する部分をドラッグで選択するか、設定の入力欄全体を翻訳するアプリから {0} を外してください。"),
        ["Read-only text · press any key or click to close"] = ("읽기 전용 글 · 아무 키나 누르거나 클릭하면 닫힙니다", "只读文本 · 按任意键或点击即可关闭", "読み取り専用のテキスト · 何かキーを押すかクリックで閉じます"),
        ["Read-only translation bubble"] = ("읽기 전용 번역 말풍선", "只读文本翻译气泡", "読み取り専用の翻訳吹き出し"),
        ["White background, black text"] = ("흰 배경 + 검정 글자", "白底黑字", "白い背景 + 黒い文字"),
        ["Black background, white text"] = ("검정 배경 + 흰 글자", "黑底白字", "黒い背景 + 白い文字"),
        ["Bubble position"] = ("말풍선 위치", "气泡位置", "吹き出しの位置"),
        ["Follows the mouse"] = ("마우스를 따라다님", "跟随鼠标", "マウスに追従"),
        ["Right above or below the selected text"] = ("선택한 글 바로 위·아래", "选中文本的正上方或正下方", "選択したテキストのすぐ上・下"),
        ["My language"] = ("내 언어", "我的语言", "自分の言語"),
        ["Text in your language goes to the hotkey's language; text in any other language comes to your language. For example, Korean becomes English and English becomes Korean."] =
            ("내 언어로 쓴 글은 단축키 언어로, 그 밖의 언어는 내 언어로 번역합니다. 예: 한국어는 영어로, 영어는 한국어로.",
             "我的语言写的文本译成快捷键的语言，其他语言的文本译成我的语言。例如：中文译成英文，英文译成中文。",
             "自分の言語の文章はショートカットの言語に、それ以外の言語は自分の言語に翻訳します。例：日本語は英語に、英語は日本語に。"),
    };
}
