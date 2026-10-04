# Vocabulary Files

單字 JSON 適合編輯、分享或擴充詞彙；備份 ZIP 保存單字與複習資料庫。App「設定與備份」提供兩種格式的匯入／匯出。

[回到主頁](../README.md#documentation) · [教材說明](../content/README.md) · [JSON 範例](../content/vocabulary-example.json)

## Data Location

預設使用 `%LOCALAPPDATA%\Wording\wording.db`。設定另存於資料目錄的 `settings.json`。

使用 `--data-dir <路徑>` 啟動時，改用指定目錄。實際位置可在「設定與備份」查看。

SQLite 資料庫包含單字與複習紀錄。交給 AI 修改詞彙時使用匯出的 JSON；資料庫應透過 App 或備份功能管理。

備份 ZIP 不包含 `settings.json`，備份資訊中的應用名稱必須為 `Wording`。從備份還原會替換整個單字庫，並先在資料目錄的 `backups` 保存還原前的資料庫備份。

## Import & Export

新安裝的詞庫為空，教材需自行匯入。需要選用作者整理的 TOEIC 詞彙時，在「設定與備份」匯入 [toeic-vocabulary.json](../content/toeic-vocabulary.json)。JSON 不包含複習進度：新資料庫從未學開始，既有詞義則依 ID 更新內容並保留自己的進度。

已使用原詞庫時，可匯入 [toeic-phrasal-verbs.json](../content/toeic-phrasal-verbs.json)，新增 62 個詞義並替既有 40 筆追加「動詞片語」分類；保留情境主題，且省略星號設定以保留自己的星號。若曾修改這 40 筆的個人內容，請先依 [動詞片語匯入說明](../content/verb-phrases.md#import) 合併，再匯入。

也可以關閉 App 後，從專案根目錄執行：

```powershell
# 匯出後可請 AI 修改 vocabulary.json，再匯入更新。
.\scripts\vocabulary.ps1 export .\vocabulary.json
.\scripts\vocabulary.ps1 import .\vocabulary.json

# 新增單字可從範例複製，修改後匯入。
.\scripts\vocabulary.ps1 import .\content\vocabulary-example.json

# 匯入選用 TOEIC 詞庫。
.\scripts\vocabulary.ps1 import .\content\toeic-vocabulary.json
```

腳本預設使用這個專案 Release 編譯出的 Wording.exe。可用 `-Executable '完整路徑\Wording.exe'` 指定其他版本，或用 `-DataDirectory '獨立資料目錄'` 選擇測試庫。啟動參數也可直接使用 `--import-vocabulary 檔案`、`--export-vocabulary 檔案`、`--result-file 結果.json`。

## JSON Format

JSON 的外層為 `version: 1` 與 `items` 陣列；每一筆是一個詞條。第一組解釋使用原本欄位，其他解釋放在 `additionalSenses` 陣列；各組可各有詞性、解釋、例句與筆記，共用詞條的星號與複習卡。舊格式的同名項目匯入時會自動合成一個。

| 欄位 | 用途 |
| --- | --- |
| `headword`、`partOfSpeech`、`meaning` | 必填：單字或片語、詞性、中文解釋 |
| `id` | 詞義的穩定 ID；修改既有詞義時保留，新詞可省略 |
| `wordId` | 共用詞條 ID，由詞條文字自動計算；同名詞義共用，可省略，不用自行配號。修改詞條時 App 會重新計算 |
| `englishDefinition`、`cue`、`level`、`notes` | 英文解釋、提示、建議程度、學習筆記；可省略 |
| `categories`、`collocations`、`synonyms` | 主題、搭配、同義詞陣列；可省略 |
| `examples` | 例句陣列；每筆 `english` 必填，`chinese` 可留空；整個陣列可省略 |
| `isStarred` | 星號；`true`／`false` 更新，省略或 `null` 保留既有設定 |
| `additionalSenses` | 其他解釋陣列；每組包含 `id`、`partOfSpeech`、`meaning` 及解釋／例句等內容欄位，沒有獨立星號或排程 |

可直接從 [vocabulary-example.json](../content/vocabulary-example.json) 複製格式，再填入自己的內容。

## Update Rules

- 修改既有詞義時，保留匯出檔中的 `id`。同一 ID 更新內容，保留排程、複習歷史、暫停及封存狀態。
- 同名詞條忽略大小寫、前後空白及連續空白後合成一個。更新版 App 啟動時先自動備份，再合併既有同名資料；受影響詞條重設為未學習，其他資料保持原狀。不必重新匯入。
- 增加另一組意思也會先備份並重設該詞條，重複匯入相同意思則保留新的學習進度。備份位於資料目錄的 `backups/before-word-merge-*.wtbackup`。
- 新詞可省略 `id`；系統以單字、詞性及中文解釋產生穩定 ID，因此原檔重複匯入不會重複新增。之後若要修改詞義，先匯出取得 ID。
- 星號用 `isStarred: true`／`false` 指定；省略或設為 `null` 時保留既有星號，新詞則預設沒有星號。匯出包含星號，方便跨資料庫移轉。星號與內容一起寫入，匯入失敗不會只改到其中一部分。
- 未出現在匯入檔的詞義不會被刪除。匯入是完整內容更新，省略的可選內容欄位會回到預設值；編輯既有詞義時請保留要留下的欄位。
- 全份檔案驗證成功後才一起寫入；任一筆無效時全部不寫入。
- JSON 僅供詞彙編輯，不含複習排程。完整備份仍用 App 的 ZIP 備份功能。

## AI Editing

可給 AI 的指示：

> 依照 content/vocabulary-example.json 的格式新增詞義，使用繁體中文，填中英文解釋、例句、同義詞與主題；若修改匯出內容，保留原有 id 及其他要留下的欄位。
