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
        ["Pause hotkeys"] = ("단축키 일시 중지", "暂停快捷键", "ショートカットを一時停止"),
        ["Paused"] = ("일시 중지됨", "已暂停", "一時停止中"),
        ["Exit"] = ("종료", "退出", "終了"),
        ["Update to {0}"] = ("{0}(으)로 업데이트", "更新到 {0}", "{0} に更新"),
        ["{0} is already used by another program. Choose another hotkey in Settings."] =
            ("{0}을(를) 다른 프로그램이 이미 쓰고 있습니다. 설정에서 다른 단축키를 고르세요.",
             "{0} 已被其他程序占用。请在设置中选择其他快捷键。",
             "{0} は別のプログラムが使用しています。設定で別のショートカットを選んでください。"),
        ["Select the text to translate first, then press the hotkey."] =
            ("번역할 텍스트를 먼저 선택한 뒤 단축키를 누르세요.", "请先选择要翻译的文本，再按快捷键。", "翻訳するテキストを選択してからショートカットを押してください。"),
        ["Translating to {0}…"] = ("{0} 번역 중…", "正在翻译为{0}…", "{0}に翻訳中…"),
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
        ["Select text and press a hotkey: it is replaced with the translation in place."] =
            ("텍스트를 선택하고 단축키를 누르면 그 자리에서 번역문으로 바뀝니다.",
             "选择文本并按快捷键，文本会被原地替换为译文。",
             "テキストを選択してショートカットを押すと、その場で訳文に置き換わります。"),
        ["Tone"] = ("말투", "语气", "トーン"),
        ["Match the original"] = ("원문 그대로", "保持原文语气", "原文に合わせる"),
        ["Casual"] = ("편하게", "随意", "カジュアル"),
        ["Polite"] = ("정중하게", "礼貌", "丁寧"),
        ["Apps that translate the whole input box"] = ("입력창 전체를 번역할 앱", "翻译整个输入框的应用", "入力欄全体を翻訳するアプリ"),
        ["In these apps, pressing the hotkey with nothing selected translates everything in the input box. Separate with commas."] =
            ("이 앱에서는 아무것도 선택하지 않고 단축키를 누르면 입력창 전체를 번역합니다. 쉼표로 구분합니다.",
             "在这些应用中，未选择文本时按快捷键会翻译输入框中的全部内容。用逗号分隔。",
             "これらのアプリでは、何も選択せずにショートカットを押すと入力欄全体を翻訳します。カンマで区切ります。"),
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
    };
}
