# Wording

**一個詞義，一點進步。**

Windows 英文單字學習 App，將自己的詞庫、雙語例句與 FSRS 間隔複習放在一起。以 C#、WPF 與 SQLite 開發，資料保存在本機；新安裝從空白詞庫開始，可自行新增或匯入 TOEIC 學習詞彙。

[下載程式](https://github.com/ming0071/Wording/releases) · [選用單字庫](content/toeic-vocabulary.json) · [文件](docs/README.md) · [Build & Tests](https://github.com/ming0071/Wording/actions)

## Features

- **間隔複習**：到期卡優先，依每日新詞設定提供新卡；可選擇一個或多個主題。
- **自己的單字庫**：多詞義、中英文解釋、雙語例句、搭配、同義詞與學習筆記。
- **收藏與整理**：星號、主題篩選，以及建立時間、字母或熟練程度排序。
- **鍵盤與發音**：空白鍵翻卡、1–4 評分、S／E 朗讀；支援自訂按鍵與自動朗讀。
- **每日成果**：近一年的複習熱力圖、每日詞義數與連續學習天數。
- **字典與 AI**：內嵌字典查閱，Codex 產生內容後先預覽，再決定是否套用。
- **資料可攜**：單字 JSON 匯入／匯出，完整詞庫與複習紀錄 ZIP 備份。

## Installation

### Windows 程式包

從 [Releases](https://github.com/ming0071/Wording/releases) 下載 Windows x64 ZIP，完整解壓後執行 `Wording.exe`。不需安裝 .NET SDK。

### 從原始碼執行

需要 Windows 11 x64、Git 與 **.NET 10 SDK**。在 PowerShell 執行：

```powershell
git clone https://github.com/ming0071/Wording.git
cd Wording
.\scripts\build.ps1
.\src\Wording.Desktop\bin\Release\net10.0-windows\Wording.exe
```

`build.ps1` 包含建置與測試；後續直接開啟 EXE。內嵌字典需要 Microsoft Edge WebView2 Runtime。

## Usage

**匯入單字庫 → 篩選主題 → 翻卡與評分 → 查看學習成果**

![Wording 操作示範：匯入詞庫、選擇主題、翻卡評分與今日成果](docs/images/wording-demo.gif)

選用詞庫：[toeic-vocabulary.json](content/toeic-vocabulary.json)（1,323 個詞義、8 個主題，含例句、學習筆記與星號）。從「設定與備份」匯入，複習進度從自己的新詞開始。

| 格式 | 適合用途 |
| --- | --- |
| 單字 JSON | 分享、編輯或擴充詞彙，包含星號，不含複習進度 |
| 備份 ZIP | 保存完整詞庫與複習紀錄；還原時整庫替換 |

資料的實際位置可在「設定與備份」查看。[JSON 格式與檔案操作](docs/vocabulary-files.md) · [教材說明](content/README.md)

### Codex（選用）

安裝 Codex CLI 並完成登入，再到 App 設定頁指定執行檔並檢查連線。

```powershell
codex login
codex login status
```

生成使用自己的 Codex 訂閱額度；一般單字管理與複習可離線使用。Windows 朗讀使用電腦上的英文合成語音。

## Tests

在專案根目錄執行：

```powershell
.\scripts\test.ps1
```

測試使用獨立資料庫，不會修改自己的單字庫。

## Project Structure

```text
Wording/
├── src/
│   ├── Wording.Core/             # 資料模型與服務契約
│   ├── Wording.Infrastructure/   # SQLite、FSRS、備份、Codex、語音
│   └── Wording.Desktop/          # WPF 畫面、ViewModel 與操作
├── content/                     # 選用教材與 JSON 範例
├── docs/                        # 資料格式、排程說明、GIF 與授權
├── scripts/                     # 建置、測試、打包與詞庫檔案工具
├── tests/
│   └── Wording.Tests/           # 自動測試與參考資料
├── .github/workflows/           # Windows CI
├── Wording.sln
└── README.md
```

[維護文件](docs/README.md) · [桌面介面導覽](src/Wording.Desktop/README.md) · [第三方授權](THIRD_PARTY_NOTICES.md)
