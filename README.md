<p align="center"><img src="src/InlineTranslator/Assets/app.png" width="96" alt="Inline Translator 아이콘"></p>

<h1 align="center">Inline Translator</h1>

<p align="center"><b>쓰던 자리에서 번역하고, 읽을 땐 말풍선으로.</b></p>

<p align="center">
  <a href="https://github.com/HSoo-Kim/inline-translator/releases/latest/download/inline-translator-win-x64.zip"><img src="https://img.shields.io/badge/Download-Windows%20x64-000000?style=for-the-badge" alt="Windows x64용 Inline Translator 다운로드"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/releases/latest"><img src="https://img.shields.io/github/v/release/HSoo-Kim/inline-translator?style=for-the-badge&label=Release&color=007ec6" alt="최신 릴리즈"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/releases"><img src="https://img.shields.io/github/downloads/HSoo-Kim/inline-translator/total?style=for-the-badge&label=Downloads&color=1E90FF" alt="GitHub 릴리즈 누적 다운로드"></a>
  <a href="https://github.com/HSoo-Kim/inline-translator/stargazers"><img src="https://img.shields.io/github/stars/HSoo-Kim/inline-translator?style=for-the-badge&label=Stars&logo=github&logoColor=white&color=000000" alt="GitHub Stars"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-3DA639?style=for-the-badge" alt="MIT 라이선스"></a>
</p>

<p align="center"><b>한국어</b> · <a href="README.en.md">English</a> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a></p>
<p align="center"><sub>Windows 11 x64 · .NET 포함 · Claude Code 또는 Codex CLI 필요</sub></p>

선택한 글을 단축키로 번역해 입력하던 곳에 넣거나, 읽기 전용 텍스트의 번역을 말풍선으로 볼 수 있습니다.
원문 언어는 자동으로 판단하며, 번역할 언어는 설정에서 고릅니다.

<p align="center"><img src="docs/images/demo-ko.gif" width="800" alt="메신저 입력창에 한글을 쓰고 Ctrl+Alt+T를 누르자 영어로 바뀌는 시연"></p>
<p align="center"><sub>실제 Claude Haiku 번역 결과로 재현한 시연 화면입니다.</sub></p>

## 이렇게 씁니다

| 이렇게 하면 | 이렇게 번역됩니다 |
|---|---|
| 💬 메신저 입력창에서 **그냥 `Ctrl+Alt+T`** | 입력창에 쓴 **글 전체**를 번역 |
| 🖱️ **드래그로 선택한 뒤 `Ctrl+Alt+T`** | **선택한 부분만** 번역 (메신저, 메일, 문서 등) |
| **드래그로 선택한 뒤 `Ctrl+Alt+R`** | **양방향 읽기 번역**. 원문을 바꾸지 않고 말풍선으로 표시 |
| **번역 중 `Esc`** | 진행 중인 번역 취소 |

- 번역 결과가 그 자리에 들어갑니다. 마음에 안 들면 `Ctrl+Z`로 되돌릴 수 있습니다. 소요 시간은 엔진과 입력에 따라 달라집니다.
- 선택 없이 전체를 번역하는 앱: 텔레그램, 카카오톡, 슬랙, 디스코드, 왓츠앱, 라인, Teams, Signal. 설정에서 더하거나 뺄 수 있습니다.
- 그 밖의 앱(Word, 브라우저 등)에서는 문서 전체가 바뀌는 일이 없도록 선택한 부분만 번역합니다.

## 시작하기

1. **번역 엔진 준비** — 둘 중 하나 이상이 설치·로그인돼 있어야 합니다.
   - [Claude Code](https://docs.anthropic.com/en/docs/claude-code): 설치 후 터미널에서 `claude` 실행 → 로그인
   - [Codex CLI](https://github.com/openai/codex): 설치 후 `codex login`
2. **[최신 버전 다운로드](https://github.com/HSoo-Kim/inline-translator/releases/latest)** → `inline-translator-win-x64.zip`을 풀고 `InlineTranslator.exe` 실행.
3. 처음 실행하면 설정 창이 뜹니다. `엔진` 탭에서 `테스트`로 번역을 확인하고, `번역` 탭에서 언어·단축키를 설정한 뒤 `저장`.

이후에는 알림 영역(시계 옆)에서 조용히 실행됩니다. 아이콘을 클릭하면 설정, 우클릭하면 일시 중지·종료 메뉴가 나옵니다.

Windows 11 x64용이고, 실행 파일에 .NET이 포함돼 있어 따로 설치할 것은 없습니다. 코드 서명이 없어서 처음 실행할 때 Windows가 경고를 띄울 수 있습니다.

## 기능

- **API 키 필요 없음** — 이미 쓰고 있는 Claude Code나 Codex 구독을 그대로 씁니다.
- **모델 선택** — Haiku 5.5, Sonnet 5.5, Opus 5.5, Fable 5, GPT 계열 등 원하는 모델로 번역합니다.
- **예비 엔진** — 기본 엔진이 실패하면 다른 엔진으로 자동으로 넘어갑니다.
- **로그인 상태 표시** — 로그인된 엔진은 초록색으로 보입니다. 로그인이 필요하면 버튼 한 번으로 로그인 창이 열리고, 번역하다 로그인이 만료돼도 알림을 누르면 바로 다시 로그인합니다.
- **앱 고르기** — 입력창 전체를 번역할 메신저는 이 PC에 설치된 앱 목록에서 골라 넣습니다. 실수로 문서 편집기를 넣어도 문서 전체가 바뀌지 않도록 막아 줍니다.
- **19개 언어** — 영어, 한국어, 일본어, 중국어(간체·번체), 스페인어, 프랑스어, 독일어, 포르투갈어, 이탈리아어, 러시아어, 베트남어, 태국어, 인도네시아어, 아랍어, 힌디어, 튀르키예어, 네덜란드어, 폴란드어. 원문 언어는 자동으로 알아냅니다.
- **단축키 3개** — 예를 들어 `Ctrl+Alt+T`는 영어, `Ctrl+Alt+J`는 일본어처럼 따로 지정합니다.
- **양방향 번역** — 내 언어로 쓴 글은 단축키 언어로, 그 밖의 언어는 내 언어로. 단축키 하나로 한국어는 영어로, 영어·일본어 등은 한국어로 바뀝니다. "내 언어"는 설정에서 바꿉니다.
- **말투 선택** — 원문 그대로, 편하게, 정중하게.
- **읽기 전용 글은 말풍선으로** — 받은 메시지, 웹페이지 본문, PDF처럼 고칠 수 없는 글을 드래그하고 단축키를 누르면, 번역을 작은 말풍선으로 보여줍니다. 아무 키나 누르거나 클릭하면 바로 사라집니다. 흰 배경·검정 글자와 검정 배경·흰 글자 중에서 고를 수 있고, 위치는 마우스를 따라다니게(기본) 하거나 선택한 글 바로 위·아래에 고정할 수 있습니다.
- **입력 보호** — 번역 중 타이핑하거나 클릭해 커서를 옮기면 원문을 덮어쓰지 않고 결과만 말풍선으로 보여줍니다. 붙여넣기 직전에도 선택한 글이 그대로인지 확인합니다. 입력창 전체 번역도 새로 쓴 글을 다시 전체 선택해 덮어쓰지 않습니다.
- **읽기 전용 단축키** — `Ctrl+Alt+R`도 양방향입니다. 내 언어로 된 글은 읽기 설정에서 선택한 언어로, 다른 언어의 글은 내 언어로 번역해 말풍선만 보여줍니다. 원문에 붙여넣지 않습니다. 설정에서 번역 언어·단축키를 바꾸거나 끌 수 있습니다.
- **Esc로 취소** — 번역 중 `Esc`를 누르면 실행 중인 엔진을 중단합니다. 취소 때문에 예비 엔진을 실행하거나 오류를 기록하지 않습니다. 번역이 끝나면 `Esc`는 원래 앱에서 평소대로 작동합니다.
- **기록 최소화** — 엔진 세션 저장을 끄고 임시 파일은 작업 후 삭제를 시도합니다. 클립보드와 오류 기록에 관한 주의사항은 아래를 참고하세요.
- **원클릭 업데이트** — 새 버전이 나오면 알림 영역 메뉴와 설정 창에 `업데이트` 버튼이 뜹니다. 누르면 받아서 바꾸고 다시 실행합니다.
- **화면 언어** — 한국어, English, 简体中文, 日本語. 일반 탭에서 저장하거나 알림 영역 메뉴에서 바꿉니다.

## 설정

업데이트는 실행 시와 5분마다, 설정 창·트레이 메뉴를 열 때 확인합니다. 바로 확인하려면 **일반 → 지금 업데이트 확인** 또는 **트레이 우클릭 → 지금 업데이트 확인**을 누르세요. 자동 확인을 꺼도 수동 확인은 가능하며, 확인 중·최신 버전·새 버전·실패 상태를 설정 안에서 볼 수 있습니다.

설정 창은 가장자리를 드래그해 크기를 바꾸거나 최대화할 수 있습니다. 작은 화면·원격 접속에서는 내용 영역을 스크롤하면 되고, 저장·취소 버튼은 하단에 고정됩니다. 화면 해상도나 배율이 바뀌면 창을 현재 작업 영역 안으로 맞춥니다.

`번역 / 엔진 / 일반` 세 탭으로 나뉩니다. 탭을 옮겨도 작성 중인 설정은 유지됩니다. 좁은 창에서는 항목을 위아래로 배치해 가로 스크롤을 없앴습니다. 단축키 오류는 해당 입력칸 아래에, 엔진 테스트 결과는 해당 엔진 아래에 표시합니다. `저장`은 변경한 내용이 있고 오류가 없을 때만 켜집니다.

<p align="center"><img src="docs/images/settings-ko.png" width="520" alt="Inline Translator 설정 창"></p>

| 탭 | 내용 |
|---|---|
| 번역 | 내 언어, 쓰기 번역 단축키·언어, 말투, 읽기 번역 단축키(`Ctrl+Alt+R`) |
| 엔진 | 기본·예비 엔진과 모델, 로그인 상태, 엔진별 번역 테스트 |
| 일반 | 입력창 전체 번역 앱, 말풍선 모양·위치, 화면 언어, Windows 시작 시 실행, 업데이트 |

화면 언어도 다른 설정과 함께 저장해야 적용됩니다. 언어를 고르다가 다른 변경 사항을 잃지 않도록 했습니다.

## 데이터와 개인정보

- 번역할 글은 **선택한 엔진(Claude Code 또는 Codex)**으로 보내지며, 해당 제공자의 데이터 정책이 적용됩니다.
- 엔진은 세션 저장을 끄고(`--no-session-persistence`, `--ephemeral`) 도구 사용을 제한해 임시 폴더에서 실행합니다. Codex는 번역 결과를 임시 파일에 기록합니다. 앱은 작업 후 폴더 삭제를 시도하지만, 삭제 실패나 비정상 종료 시 파일이 남을 수 있습니다.
- 선택한 원문을 읽을 때 복사 기능을 사용하므로 원문이 Windows 클립보드 기록이나 다른 클립보드 관리자에 남을 수 있습니다. 결과를 붙여 넣을 때는 Windows 기록·클라우드 동기화 제외를 요청하고, 가능한 경우 기존 클립보드를 복원합니다.
- 오류 기록(`%LOCALAPPDATA%\InlineTranslator\error.log`)에는 엔진의 오류 메시지가 포함될 수 있습니다. 엔진이 오류 메시지에 텍스트를 포함하면 해당 내용도 기록될 수 있습니다.
- 새 버전 확인은 GitHub(`api.github.com`)에서 최신 릴리스 정보만 읽습니다. 업데이트 파일은 GitHub가 알려 주는 SHA-256과 맞을 때만 설치합니다. 설정에서 끌 수 있습니다.
- 설정 파일: `%LOCALAPPDATA%\InlineTranslator\settings.json`
- 번역은 쓰고 있는 구독의 사용량으로 계산됩니다.

## 알아 둘 점

- **읽기 형식**: 파일을 직접 여는 것이 아니라 뷰어가 제공하는 선택 텍스트를 번역합니다. 텍스트 PDF·웹페이지·TXT/Markdown·오피스 문서·메일 등에서 텍스트 복사가 가능하면 사용할 수 있습니다. 유니코드·일반 텍스트와 RTF만 제공하는 복사를 지원합니다. 이미지 스캔·복사 금지 텍스트는 대상이 아니며 OCR·파일 전체 변환 기능은 없습니다.
- **PDF**: 실제 글자를 선택하고 `Ctrl+Alt+R`을 누르면 됩니다. 편집 도구 팝업을 먼저 닫을 필요는 없습니다. 느린 PDF 복사를 최대 2초 기다립니다.
- **쓰기**: 느린 편집기의 선택 복사·재확인을 최대 2초 기다리되, 등록된 메신저의 무선택 판별은 빠르게 유지합니다. 결과는 일반 텍스트이며 문서 서식·스프레드시트 배치를 변환하는 기능은 아닙니다. 클립보드 기록 후에도 포커스를 재확인하며, 기존 클립보드를 보관할 수 없으면 복사 전에 중단합니다.
- Claude Code 2.1.300, Codex CLI 0.153.2에서 확인했습니다. 너무 오래된 버전에서는 일부 옵션이 없어 실패할 수 있습니다.
- 코드 편집기 등 일부 앱은 선택 없이 `Ctrl+C`를 누르면 줄 전체를 복사합니다. 이런 앱에서는 번역할 부분을 먼저 선택하세요.
- 관리자 권한으로 실행 중인 창에는 Windows 보안 정책상 붙여 넣을 수 없습니다.
- 다른 프로그램이 같은 단축키를 쓰고 있으면 알려 드립니다. 설정에서 다른 조합을 고르세요.

## 소스 빌드

필수: Windows 11 x64, .NET 10 SDK.

```powershell
.\build.ps1                 # 빌드 + 단위 테스트
.\build.ps1 -Screenshots    # 설정 창 스크린샷을 비공개 데스크톱에서 다시 생성
.\build.ps1 -Publish        # 런타임 포함 실행 파일과 ZIP 생성
```

시연 GIF는 `docs/demo/demo.html`을 프레임별로 렌더해 만들었습니다.

```
src/InlineTranslator/   앱 (트레이, 단축키, 번역 엔진, 설정 창, 업데이트)
tests/                  설정·단축키·엔진·업데이트 회귀 테스트
tools/Preview/          설정 창 렌더링과 실제 엔진 번역 확인
tools/PrivateDesktopQa/ 사용 중인 화면을 건드리지 않는 비공개 데스크톱 실행기
```

외부 NuGet 패키지 없이 .NET Windows Forms와 Win32 API만 사용합니다.

## 라이선스

[MIT 라이선스](LICENSE). 개인·회사 모두 무료로 사용·수정·배포할 수 있으며, 배포할 때 저작권과 라이선스 고지를 함께 포함하면 됩니다.
