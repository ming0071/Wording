# 選用教材

Wording 新安裝從空白詞庫開始。這個目錄的檔案隨原始碼提供，但不自動載入，也不打包進程式 ZIP。需要時到 App「設定與備份」選擇單字 JSON 匯入。

| 檔案 | 內容 |
| --- | --- |
| [toeic-vocabulary.json](toeic-vocabulary.json) | 作者整理的 TOEIC 學習詞彙：1,323 個詞義、8 個主題、212 個星號詞義，包含中英文解釋、例句、同義詞與學習筆記 |
| [toeic-starter.json](toeic-starter.json) | 300 個 AI 編寫的原創詞義或片語，12 個主題；可單獨匯入，也用於教材與複習版面測試 |
| [vocabulary-example.json](vocabulary-example.json) | 編寫新詞義 JSON 的格式範例 |

`toeic-vocabulary.json` 使用 App 的單字匯出功能產生，包含作者整理的學習詞彙。沒有排程、評分、學習日期、複習紀錄、個人設定或帳號資訊；星號保留為選用的學習重點。筆記只包含學習內容。

八個主題是職場與人事、商務與法規、財務與投資、會議與溝通、行銷與客服、產品與物流、設施與環境、旅遊與生活。這是學習用途的分類，不是官方 TOEIC 題型分類。

兩份教材有部分重疊，通常選一份匯入即可。相同 ID 重複匯入會更新內容，不會另建相同詞義；更新既有庫時保留自己的學習進度，但明確提供的星號會套用。匯入不會刪除其他單字。

## 更新教材

修改既有詞義時保留 `id`；新增不同詞義時使用新的 ID。不要依行號重新配號，也不要把舊 ID 配給其他意思。語言內容仍需自行核對；本專案不代表字典背書或官方必考詞表。

關閉 App 後，可從專案根目錄更新分享檔：

```powershell
.\scripts\vocabulary.ps1 export .\content\toeic-vocabulary.json
```

此指令會用目前詞庫覆寫檔案，提交前請確認其中的筆記也是要分享的內容。JSON 欄位與匯入規則見 [檔案維護說明](../docs/vocabulary-files.md)。

原創 300 筆起始教材的結構驗證：

```powershell
python scripts/validate-content.py
```

分享詞彙的完整匯入、星號、內容往返與無作者學習紀錄檢查：

```powershell
.\scripts\test.ps1 -Filter 'FullyQualifiedName~OptionalVocabularyTests'
```
