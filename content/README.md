# Vocabulary Packs

Wording 新安裝從空白詞庫開始。這個目錄的檔案隨原始碼提供，但不自動載入，也不打包進程式 ZIP。需要時到 App「設定與備份」選擇單字 JSON 匯入。

[回到主頁](../README.md#documentation) · [JSON 格式與檔案操作](../docs/vocabulary-files.md)

| 檔案 | 內容 |
| --- | --- |
| [toeic-vocabulary.json](toeic-vocabulary.json) | TOEIC 學習詞彙：1,113 個單字／片語、1,385 組解釋、8 個情境主題與動詞片語、181 個星號詞條，包含中英文解釋、例句、同義詞與學習筆記 |
| [toeic-phrasal-verbs.json](toeic-phrasal-verbs.json) | 動詞片語補充：99 個片語、102 組解釋，其中 62 個新詞義、40 筆既有詞義追加分類；省略星號設定以保留使用者自己的星號。選詞與用法見 [動詞片語](verb-phrases.md) |
| [toeic-starter.json](toeic-starter.json) | 297 個詞條、300 組 AI 編寫的原創解釋，12 個主題；可單獨匯入，也用於教材與複習版面測試 |
| [vocabulary-example.json](vocabulary-example.json) | 編寫新詞義 JSON 的格式範例 |

`toeic-vocabulary.json` 以作者匯出的詞庫為基礎，補充原創動詞片語教材。沒有排程、評分、學習日期、複習紀錄、個人設定或帳號資訊；星號保留為選用的學習重點。筆記只包含學習內容。

## Sources

作者自行整理並匯出的詞庫，單字由《全新！新制多益 TOEIC 單字大全》中挑選，例句由網路辭典選用；AI 協助整理情境分類。這部分的辭典例句並非本專案原創，來源說明不代表取得辭典或出版社的轉載授權。

`toeic-starter.json` 的 300 組解釋與動詞片語補充另由 AI 編寫並審閱，與作者從網路辭典選用的例句分開說明。完整詞庫已包含動詞片語補充，因此同時包含作者選用內容與 AI 編寫內容。

## Import

八個情境主題是職場與人事、商務與法規、財務與投資、會議與溝通、行銷與客服、產品與物流、設施與環境、旅遊與生活。另有「動詞片語」分類，同一詞義可同時屬於情境與片語主題。這是學習用途的分類，不是官方 TOEIC 題型分類。

完整詞庫與 300 筆起始教材有部分重疊，通常選一份匯入即可。已使用原本 1,323 筆詞庫時，可只匯入動詞片語補充檔；整合後的完整詞庫已包含這 102 筆，不必再匯入補充檔。

同名詞條合成一個資料項目，多組解釋放在 `additionalSenses`；共用星號與複習卡。初次合併舊資料或新增另一組意思時，該詞條重新成為未學習；重複匯入相同內容不再重設。補充檔省略星號，不會改變自己的星號；完整詞庫提供的星號則會套用。匯入不會刪除其他單字；若自己編輯過這 40 筆既有片語，請先依 [補充檔匯入說明](verb-phrases.md#import) 合併個人內容。

## Maintenance

每個詞條只有一個外層 `id`、一個星號、一張複習卡。解釋本身的 ID 保存於巢狀內容，不再擁有獨立排程。四份 JSON 都按詞條排列；App 編輯頁載入全部解釋，複習翻卡後一起顯示並為整個詞條評分。

卡片筆記只放用法、文法提醒與學習內容；教材編寫說明及字典來源集中在文件，不重複放進每張卡。程式啟動時會先備份，再清除舊動詞片語教材的重複聲明與來源網址，保留個人筆記及既有排程。

修改既有詞義時保留 `id`；新增不同詞義時使用新的 ID。不要依行號重新配號，也不要把舊 ID 配給其他意思。語言內容仍需自行核對；本專案不代表字典背書或官方必考詞表。

關閉 App 後，可從專案根目錄更新分享檔：

```powershell
.\scripts\vocabulary.ps1 export .\content\toeic-vocabulary.json
```

此指令會用目前詞庫覆寫檔案，提交前請確認其中的筆記也是要分享的內容。

## Validation

原創 300 筆起始教材的結構驗證：

```powershell
python scripts/validate-content.py
```

分享詞彙的匯入、星號、內容往返與無作者學習紀錄檢查：

```powershell
.\scripts\test.ps1 -Filter 'FullyQualifiedName~OptionalVocabularyTests'
```
