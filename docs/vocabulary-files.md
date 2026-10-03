# 用檔案維護單字庫

預設資料庫是 `%LOCALAPPDATA%\Wording\wording.db`，設定另存於同目錄的 `settings.json`。使用 `--data-dir` 啟動的獨立資料庫位於指定目錄。實際使用的資料目錄可在 App「設定與備份」查看。

SQLite 資料庫同時包含單字與複習紀錄。給 AI 修改單字請使用 JSON，不要把 `.db` 當文字檔編輯。App「設定與備份」有單字 JSON 匯入／匯出。

新安裝的詞庫為空，專案教材不會自動載入。需要選用作者的 TOEIC 詞彙時，在設定頁匯入 `content/toeic-vocabulary.json`，或關閉 App 後執行 `scripts/vocabulary.ps1 import .\content\toeic-vocabulary.json`。JSON 包含星號但不含複習進度，匯入新資料庫時所有詞義從未學開始。既有資料庫則依 ID 更新內容，保留自己的學習進度。

也可以不開畫面，從專案根目錄執行：

```powershell
# 先關閉 Wording。匯出後可請 AI 修改 vocabulary.json。
.\scripts\vocabulary.ps1 export .\vocabulary.json
.\scripts\vocabulary.ps1 import .\vocabulary.json

# 新增單字可從範例複製，修改後匯入。
.\scripts\vocabulary.ps1 import .\content\vocabulary-example.json
```

腳本預設使用這個專案 Release 編譯出的 Wording.exe。可用 `-Executable '完整路徑\Wording.exe'` 指定其他版本，或用 `-DataDirectory '獨立資料目錄'` 選擇測試庫。啟動參數也可直接使用 `--import-vocabulary 檔案`、`--export-vocabulary 檔案`、`--result-file 結果.json`。

JSON 的外層為 `version: 1` 與 `items` 陣列；每一筆是一個詞義。同一個單字有多種意思，就新增多筆。必填 `headword`、`partOfSpeech`、`meaning`。其他文字欄位與範例相同，可省略；例句有英文必填，中文可留空。圖片這版不支援。

- 修改既有詞義時，保留匯出檔中的 `id`。同一 ID 更新內容，保留排程、複習歷史、暫停及封存狀態。
- 新詞可省略 `id`；系統以單字、詞性及中文解釋產生穩定 ID，因此原檔重複匯入不會重複新增。之後若要修改詞義，先匯出取得 ID。
- 星號用 `isStarred: true`／`false` 指定；省略或設為 `null` 時保留既有星號，新詞則預設沒有星號。匯出包含星號，方便跨資料庫移轉。星號與內容一起寫入，匯入失敗不會只改到其中一部分。
- 未出現在匯入檔的詞義不會被刪除。匯入是完整內容更新，省略的可選欄位會變成空值；編輯既有詞義時請保留要留下的欄位。
- 全份檔案驗證成功後才一起寫入；任一筆無效時全部不寫入。
- JSON 僅供詞彙編輯，不含複習排程。完整備份仍用 App 的 ZIP 備份功能。

可給 AI 的指示：「依照 content/vocabulary-example.json 的格式新增詞義，使用繁體中文，填中英文解釋、例句、同義詞與主題；若修改匯出內容，保留原有 id 及其他要留下的欄位。」
