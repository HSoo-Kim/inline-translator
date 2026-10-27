<p align="center"><img src="src/InlineTranslator/Assets/app.png" width="96" alt="Inline Translator 图标"></p>

<h1 align="center">Inline Translator</h1>

<p align="center"><b>在输入处直接翻译，阅读时用气泡查看。</b></p>

<p align="center">
  <a href="https://github.com/HSoo-Kim/inline-translator/releases/latest/download/inline-translator-win-x64.zip"><img src="https://img.shields.io/badge/Download-Windows%20x64-000000?style=for-the-badge" alt="下载 Windows x64 版 Inline Translator"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/releases/latest"><img src="https://img.shields.io/github/v/release/HSoo-Kim/inline-translator?style=for-the-badge&label=Release&color=007ec6" alt="最新发布版本"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/releases"><img src="https://img.shields.io/github/downloads/HSoo-Kim/inline-translator/total?style=for-the-badge&label=Downloads&color=1E90FF" alt="GitHub Release 累计下载量"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/stargazers"><img src="https://img.shields.io/github/stars/HSoo-Kim/inline-translator?style=for-the-badge&label=Stars&logo=github&logoColor=white&color=000000" alt="GitHub Stars"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-3DA639?style=for-the-badge" alt="MIT 许可证"></a>
</p>

<p align="center"><a href="README.md">한국어</a> · <a href="README.en.md">English</a> · <b>简体中文</b> · <a href="README.ja.md">日本語</a></p>
<p align="center"><sub>Windows 11 x64 · 内含 .NET · 需要 Claude Code 或 Codex CLI</sub></p>

用快捷键原地翻译选中的文本，或在气泡中查看不可编辑文本的译文。
原文语言会自动判断，目标语言可在设置中选择。

<p align="center"><img src="docs/images/demo-en.gif" width="800" alt="在聊天输入框输入韩文后按 Ctrl+Alt+T，文本变成英文"></p>
<p align="center"><sub>使用真实 Claude Haiku 翻译结果重现的演示。</sub></p>

## 使用方法

| 这样操作 | 翻译结果 |
|---|---|
| 💬 在聊天输入框中**直接按 `Ctrl+Alt+T`** | 翻译输入框中的**全部内容** |
| 🖱️ **拖动选中后按 `Ctrl+Alt+T`** | **只翻译选中的部分**（聊天、邮件、文档等） |
| **选中文字后按 `Ctrl+Alt+R`** | **双向阅读翻译**，只显示气泡，不修改原文 |
| **翻译中按 `Esc`** | 取消当前翻译 |

- 译文会替换原文，不满意可按 `Ctrl+Z` 撤销。翻译时间因引擎和输入内容而异。
- 不选中即翻译整个输入框的应用：Telegram、KakaoTalk、Slack、Discord、WhatsApp、LINE、Teams、Signal。可在设置中增减。
- 其他应用（Word、浏览器等）只翻译选中部分，不会误换整个文档。

## 开始使用

1. **准备翻译引擎** —— 至少安装并登录以下之一：
   - [Claude Code](https://docs.anthropic.com/en/docs/claude-code)：安装后在终端运行 `claude` 并登录
   - [Codex CLI](https://github.com/openai/codex)：安装后运行 `codex login`
2. **[下载最新版本](https://github.com/HSoo-Kim/inline-translator/releases/latest)**，解压 `inline-translator-win-x64.zip`，运行 `InlineTranslator.exe`。
3. 首次运行会打开设置。在“引擎”中测试翻译，在“翻译”中选择语言和快捷键，然后“保存”。

之后它会在通知区域静默运行。单击图标打开设置，右键可暂停或退出。

适用于 Windows 11 x64，可执行文件已包含 .NET，无需额外安装。尚未进行代码签名，首次运行时 Windows 可能会提示警告。

## 功能

- **无需 API 密钥** —— 直接使用你已有的 Claude Code 或 Codex 订阅。
- **选择模型** —— Haiku 5.5、Sonnet 5.5、Opus 5.5、Fable 5、GPT 系列，也可以直接输入模型名。
- **备用引擎** —— 主引擎失败时自动切换。
- **登录状态** —— 已登录的引擎显示为绿色。需要登录时一键打开登录窗口；翻译时登录过期也会提示，点击即可重新登录。
- **从列表选择应用** —— 从本机已安装的应用中选择要整框翻译的聊天应用。即使误加了文档编辑器，也不会替换整篇文档。
- **19 种语言** —— 英语、韩语、日语、中文（简体/繁体）、西班牙语、法语、德语、葡萄牙语、意大利语、俄语、越南语、泰语、印尼语、阿拉伯语、印地语、土耳其语、荷兰语、波兰语。自动识别原文语言。
- **3 个快捷键** —— 例如 `Ctrl+Alt+T` 译成英语，`Ctrl+Alt+J` 译成日语。
- **双向翻译** —— 我的语言写的文本译成快捷键的语言，其他语言的文本译成我的语言。一个快捷键即可中译英、英（日等）译中。“我的语言”可在设置中更改。
- **语气** —— 保持原文语气、随意、礼貌。
- **只读文本弹出显示** —— 选中无法编辑的文字（收到的消息、网页正文、PDF）后按快捷键，译文会显示在小气泡中，按任意键或点击即消失。可选白底黑字或黑底白字，位置可选跟随鼠标（默认）或固定在选中文本的正上方/正下方。
- **保护输入** —— 翻译期间输入文字或点击移动光标后，不覆盖原文，只显示翻译气泡。粘贴前会再次确认选中的文字未改变。整框翻译也不会重新全选并覆盖新输入的内容。
- **阅读快捷键** —— `Ctrl+Alt+R` 也支持双向翻译。我的语言写的文本译成阅读设置中所选的语言，其他语言译成我的语言。只显示气泡，不粘贴。可在设置中选择目标语言、更改快捷键或关闭。
- **Esc 取消** —— 翻译中按 `Esc` 停止正在运行的引擎，不启动备用引擎，也不记录错误。翻译结束后，Esc 恢复在原应用中的正常作用。
- **尽量减少记录** —— 关闭引擎会话保存，并在使用后尝试删除临时文件。剪贴板和错误日志的注意事项见下文。
- **一键更新** —— 有新版本时，托盘菜单和设置中会出现“更新”按钮，点击即可下载、替换并重启。
- **界面语言** —— 한국어、English、简体中文、日本語。可在“常规”中选择并保存，或从托盘菜单切换。

## 设置

启动时、每五分钟，以及打开设置或托盘菜单时都会检查更新。也可使用“常规 → 立即检查更新”或托盘菜单中的同名操作。关闭自动检查后仍可手动检查，设置内直接显示检查进度、当前或新版本及错误。

设置窗口可拖动边框调整大小，也可最大化。在小屏幕或远程桌面中滚动内容即可，保存和取消按钮固定在底部。分辨率或缩放变化时，窗口会调整到当前工作区域内。

设置分为“翻译 / 引擎 / 常规”三个标签页，切换时保留未保存的修改。窄窗口中控件上下排列，无需横向滚动。快捷键错误显示在对应输入框下，每个引擎分别显示测试进度和结果。只有修改了设置且没有错误时，“保存”才可用。

<p align="center"><img src="docs/images/settings-zh-CN.png" width="520" alt="Inline Translator 设置窗口"></p>

| 标签页 | 内容 |
|---|---|
| 翻译 | 我的语言、输入翻译快捷键及目标语言、语气、阅读快捷键（`Ctrl+Alt+R`） |
| 引擎 | 主用和备用引擎、模型、登录状态、分别测试 |
| 常规 | 整框翻译应用、气泡外观及位置、界面语言、开机启动、更新 |

界面语言与其他设置一同保存后生效，避免选择语言时丢失其他修改。

## 数据与隐私

- 文本会发送给**你选择的引擎**（Claude Code 或 Codex），并适用该服务提供商的数据政策。
- 引擎在临时文件夹中运行，关闭会话保存（`--no-session-persistence`、`--ephemeral`）并限制工具使用。Codex 会将译文写入临时文件。应用会在结束后尝试删除文件夹，但清理失败或异常退出时文件可能保留。
- 读取选中的原文需要复制，因此原文可能出现在 Windows 剪贴板历史或其他剪贴板管理器中。粘贴译文时，应用会请求将其排除在 Windows 剪贴板历史和云同步之外，并在可能时恢复原剪贴板。
- 错误日志（`%LOCALAPPDATA%\InlineTranslator\error.log`）可能包含引擎的错误信息。如果错误信息中包含文本，该文本也可能被记录。
- 检查更新只从 GitHub（`api.github.com`）读取最新版本信息；只有 SHA-256 与 GitHub 公布的一致时才安装。可在设置中关闭。
- 设置文件：`%LOCALAPPDATA%\InlineTranslator\settings.json`
- 翻译会计入你的订阅用量。

## 注意事项

- **阅读格式**：翻译的是查看器提供的选中文本，而不是直接打开文件。支持允许复制文字的 PDF、网页、TXT/Markdown、办公文档和邮件等。可读取 Unicode、纯文本及仅提供 RTF 的复制内容。不支持图片扫描、禁止复制的文本、OCR 或整文件转换。
- **PDF**：选中文字后按 `Ctrl+Alt+R`，无需先关闭编辑工具弹窗。较慢的复制最多等待 2 秒。
- **输入**：较慢编辑器的选区复制与复核最多等待 2 秒，已登记聊天应用的无选区判断保持快速。输出为纯文本，不转换文档格式或电子表格布局。设置剪贴板后再次检查焦点；无法保存原剪贴板时，在复制前中止。
- 已在 Claude Code 2.1.300 和 Codex CLI 0.153.2 上验证。过旧的版本可能缺少部分选项。
- 部分应用（如代码编辑器）在未选中时按 `Ctrl+C` 会复制整行，请先选中文字。
- Windows 不允许向以管理员身份运行的窗口粘贴。
- 如果快捷键已被其他程序占用，会提示你在设置中更换。

## 从源码构建

需要 Windows 11 x64 和 .NET 10 SDK。

```powershell
.\build.ps1                 # 构建 + 单元测试
.\build.ps1 -Screenshots    # 在私有桌面重新生成设置截图
.\build.ps1 -Publish        # 生成自包含可执行文件和 ZIP
```

演示 GIF 由 `docs/demo/demo.html` 逐帧渲染生成。不依赖任何 NuGet 包，只使用 .NET Windows Forms 和 Win32 API。

## 许可证

[MIT](LICENSE)。个人和公司均可免费使用、修改和分发；再分发时请保留版权和许可声明。
