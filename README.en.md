<p align="center"><img src="src/InlineTranslator/Assets/app.png" width="96" alt="Inline Translator icon"></p>

<h1 align="center">Inline Translator</h1>

<p align="center"><b>Translate where you type. Read translations in a bubble.</b></p>

<p align="center">
  <a href="https://github.com/HSoo-Kim/inline-translator/releases/latest/download/inline-translator-win-x64.zip"><img src="https://img.shields.io/badge/Download-Windows%20x64-000000?style=for-the-badge" alt="Download Inline Translator for Windows x64"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/releases/latest"><img src="https://img.shields.io/github/v/release/HSoo-Kim/inline-translator?style=for-the-badge&label=Release&color=007ec6" alt="Latest release"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/releases"><img src="https://img.shields.io/github/downloads/HSoo-Kim/inline-translator/total?style=for-the-badge&label=Downloads&color=1E90FF" alt="Total GitHub release downloads"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/stargazers"><img src="https://img.shields.io/github/stars/HSoo-Kim/inline-translator?style=for-the-badge&label=Stars&logo=github&logoColor=white&color=000000" alt="GitHub stars"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-3DA639?style=for-the-badge" alt="MIT license"></a>
</p>

<p align="center"><a href="README.md">한국어</a> · <b>English</b> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a></p>
<p align="center"><sub>Windows 11 x64 · .NET included · Claude Code or Codex CLI required</sub></p>

Translate selected text in place with a hotkey, or read a translation of non-editable text in a bubble.
The source language is detected automatically; choose the target language in Settings.

<p align="center"><img src="docs/images/demo-en.gif" width="800" alt="Typing Korean in a chat box, pressing Ctrl+Alt+T, and the text becoming English"></p>
<p align="center"><sub>A reenactment built from a real Claude Haiku translation.</sub></p>

## How it works

| Do this | Get this |
|---|---|
| 💬 In a chat input box, **just press `Ctrl+Alt+T`** | The **whole message** you typed is translated |
| 🖱️ **Drag to select, then press `Ctrl+Alt+T`** | **Only the selection** is translated (chat, email, documents and more) |
| **Select text, then press `Ctrl+Alt+R`** | **Two-way reading translation**, showing a bubble without changing the original |
| **Press `Esc` while translating** | Cancel the current translation |

- The translation replaces your text. Use `Ctrl+Z` to undo it. Translation time varies by engine and input.
- Apps where no selection means the whole input box: Telegram, KakaoTalk, Slack, Discord, WhatsApp, LINE, Teams, Signal. Add or remove apps in Settings.
- Everywhere else (Word, browsers, …) only the selection is translated, so a whole document is never replaced by accident.

## Getting started

1. **Set up an engine** — at least one of these, installed and signed in:
   - [Claude Code](https://docs.anthropic.com/en/docs/claude-code): install, run `claude` in a terminal and sign in
   - [Codex CLI](https://github.com/openai/codex): install, then `codex login`
2. **[Download the latest release](https://github.com/HSoo-Kim/inline-translator/releases/latest)**, unzip `inline-translator-win-x64.zip` and run `InlineTranslator.exe`.
3. Settings open on first launch. Use `Test` in Engines, choose languages and hotkeys in Translation, then `Save`.

From then on it runs quietly in the notification area. Click the icon for Settings; right-click to pause or exit.

Built for Windows 11 x64. The executable includes .NET, so nothing else to install. It is not code-signed yet, so Windows may warn on first launch.

## Features

- **No API key** — uses the Claude Code or Codex subscription you already have.
- **Pick the model** — Haiku 5.5, Sonnet 5.5, Opus 5.5, Fable 5, GPT models, or any model name you type.
- **Fallback engine** — if the main engine fails, the backup takes over automatically.
- **Sign-in status** — signed-in engines show green. If one needs signing in, one click opens the sign-in window, and an expired sign-in during translation shows a notice you can click to sign in again.
- **Pick apps, don't type them** — choose the chat apps for whole-message translation from the apps installed on your PC. Add a document editor by mistake and it still won't replace a whole document.
- **19 languages** — English, Korean, Japanese, Chinese (Simplified and Traditional), Spanish, French, German, Portuguese, Italian, Russian, Vietnamese, Thai, Indonesian, Arabic, Hindi, Turkish, Dutch and Polish. The source language is detected automatically.
- **Three hotkeys** — for example `Ctrl+Alt+T` for English and `Ctrl+Alt+J` for Japanese.
- **Two-way** — text in your language goes to the hotkey's language; text in any other language comes to your language. One hotkey turns Korean into English and English (or Japanese, …) into Korean. Set "My language" in Settings.
- **Tone** — match the original, casual, or polite.
- **Read-only text pops up** — select text you cannot edit (a received message, a web page, a PDF) and press the hotkey: the translation appears in a small bubble that disappears on any key or click. Choose black-on-white or white-on-black, and whether it follows the mouse (default) or stays right above/below the selection.
- **Protect ongoing edits** — typing or clicking to move the cursor while translating prevents replacement; the result appears in a bubble instead. The selection is checked again before pasting. Whole-input translation never selects everything again and overwrites newly typed text.
- **Reading shortcut** — `Ctrl+Alt+R` works both ways too. Text in My language goes to the reading shortcut's selected language; other text comes to My language. It only shows a bubble and never pastes. Choose its target language and hotkey, or disable it, in Settings.
- **Esc to cancel** — while translating, `Esc` stops the running engine without starting the fallback or logging an error. After translation, Esc works normally in the foreground app again.
- **Minimize records** — engine session saving is disabled, and temporary files are deleted after use when possible. See the clipboard and error-log notes below.
- **One-click update** — when a new version is out, an `Update` button appears in the tray menu and Settings. Click it to download, replace and restart.
- **Interface languages** — 한국어, English, 简体中文, 日本語. Save a choice in General or switch from the tray menu.

## Settings

Updates are checked at startup, every five minutes, and when opening Settings or the tray menu. Use **General → Check for updates** or the tray's **Check for updates** action to check immediately, even with automatic checks disabled. Settings show progress, current/new version and errors directly.

Drag the window borders to resize Settings, or maximize it. On small or remote desktops, scroll the content while Save and Cancel stay fixed at the bottom. Resolution or scaling changes keep the window within the current working area.

Three tabs organize settings: Translation, Engines and General. Switching tabs keeps pending edits. Narrow windows stack fields without horizontal scrolling. Hotkey errors appear under the field; each engine has its own test progress and result. Save enables only when settings have changed and validation passes.

<p align="center"><img src="docs/images/settings-en.png" width="520" alt="Inline Translator settings window"></p>

| Tab | Contents |
|---|---|
| Translation | My language, writing hotkeys and target languages, tone, reading shortcut (`Ctrl+Alt+R`) |
| Engines | Primary and fallback providers/models, sign-in status, individual translation tests |
| General | Whole-input apps, bubble appearance/position, interface language, startup, updates |

Interface language applies with Save rather than discarding other edits when a language is selected.

## Data and privacy

- Text is sent to **the engine you chose** (Claude Code or Codex), and that provider's data policies apply.
- Engines run with session saving off (`--no-session-persistence`, `--ephemeral`) and tools restricted in a temporary folder. Codex writes the translated result to a temporary file. The app attempts to delete the folder afterward, but it may remain if cleanup fails or the app exits unexpectedly.
- Reading selected text uses the copy command, so the original may appear in Windows clipboard history or another clipboard manager. When pasting a translation, the app requests exclusion from Windows clipboard history and cloud sync and restores the previous clipboard when possible.
- The error log (`%LOCALAPPDATA%\InlineTranslator\error.log`) can include engine error messages. If an engine includes text in an error message, that text may also be logged.
- Update checks read only the latest release from GitHub (`api.github.com`). Updates install only if the SHA-256 published by GitHub matches. You can turn this off.
- Settings file: `%LOCALAPPDATA%\InlineTranslator\settings.json`
- Translations count toward your subscription usage.

## Good to know

- **Reading formats**: the app translates selected text supplied by the viewer, not the file itself. Text-layer PDFs, web pages, TXT/Markdown, office documents and email work when their apps allow copying text. Unicode/plain text and RTF-only copies are supported; image-only scans and copy-protected text are not. There is no OCR or whole-file conversion.
- **PDF**: select actual text and press `Ctrl+Alt+R`; the editing toolbar does not need to be closed first. Slow PDF copying is allowed up to two seconds.
- **Writing**: selection copying and verification allow up to two seconds in slower editors, while listed chat apps retain the fast no-selection probe. Output is plain text, not a document-format or spreadsheet-layout conversion. Paste rechecks focus after publishing the clipboard; unsupported clipboard contents that cannot be preserved stop the operation before copying.
- Tested with Claude Code 2.1.300 and Codex CLI 0.153.2. Much older versions may lack some options.
- Some apps (code editors, for example) copy the whole line when you press `Ctrl+C` with nothing selected. Select the text first there.
- Windows does not allow pasting into windows running as administrator.
- If another program already uses your hotkey, you will be told; choose a different one in Settings.

## Building from source

Requires Windows 11 x64 and the .NET 10 SDK.

```powershell
.\build.ps1                 # build + unit tests
.\build.ps1 -Screenshots    # re-render settings screenshots on a private desktop
.\build.ps1 -Publish        # self-contained executable and ZIP
```

The demo GIF is rendered frame by frame from `docs/demo/demo.html`.

```
src/InlineTranslator/   app (tray, hotkeys, engines, settings, updates)
tests/                  settings, hotkey, engine and update regression tests
tools/Preview/          settings rendering and real engine translation checks
tools/PrivateDesktopQa/ private-desktop runner that never touches your screen
```

No NuGet dependencies: .NET Windows Forms and Win32 APIs only.

## License

[MIT](LICENSE). Free for personal and commercial use, modification and distribution; keep the copyright and license notice when you redistribute.
