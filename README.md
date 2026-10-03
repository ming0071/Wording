# WordTrail

個人用的 Windows 英文單字庫。C#／WPF／SQLite，包含逐詞義間隔複習、300 筆原創教材、Windows 英文發音，以及使用既有 ChatGPT 登入的本機 Codex 文字補充。

教材以約 TOEIC 450 的學習者為出發點；不是官方必考詞表，也不提供分數保證。AI 編寫的內容有來源標示，可以修改。

## 直接使用

從 [GitHub Releases](https://github.com/ming0071/WordTrail/releases) 下載 Windows x64 ZIP，完整解壓後開啟 `WordTrail.exe`。這是自足程式包，使用時不必安裝 Visual Studio 或 .NET SDK；Codex AI 功能仍需要另外設定 CLI 登入。

自動建置的程式包與測試報告也在 [GitHub Actions](https://github.com/ming0071/WordTrail/actions)。私人專案的下載需要使用可存取此 repository 的 GitHub 帳號登入。

## 開發環境

- Windows 11 x64。
- Visual Studio 2026 18.0 或更新版本，安裝「.NET 桌面開發」工作負載；或單獨安裝 .NET 10 SDK，使用命令列建置。
- Visual Studio 2022 不能直接作為本專案 .NET 10 的支援 IDE。SDK 與 Visual Studio 是不同的工具。
- GitHub Actions 會在 Windows 上建置、測試並產生自足 ZIP，下載程式包的電腦不必另外裝 .NET SDK。

```powershell
git clone https://github.com/ming0071/WordTrail.git
cd WordTrail
./scripts/build.ps1
dotnet run --project src/WordTrail.Desktop
./scripts/publish.ps1
```

也可在 Visual Studio 開啟 `WordTrail.sln`，將 `WordTrail.Desktop` 設為啟始專案。SDK 允許 .NET 10 穩定服務更新；實際使用版本會出現在 CI 紀錄。

## 日常使用

1. 直接按「開始複習」。單字庫中的未學新詞自動可用，不必逐字選入；可選一個主題或「全部單字庫」，再按「套用」。
2. 先處理範圍內的到期卡，再學新詞；預設每日最多 5 個，可在「設定與備份」填入任意非負整數，沒有固定數量上限（0 表示只複習舊詞）。當日初始到期量 10–19 張時仍最多 2 個新詞，20 張以上先不加新詞。每日名額由所有主題共用，切換主題不會重設。
3. 先想答案，再按空白鍵翻卡，按 1／2／3／4 選「重來／困難／良好／簡單」，S 朗讀單字，E 朗讀全部英文例句；也可用滑鼠，數字鍵盤同樣可用。所有按鍵可在「設定與備份」自訂。只有自評提交會更新排程。
4. 新增時用選單選詞性，可按「新增另一個詞義」輸入同一詞條的多個意思，一次保存；每個詞義都有自己的進度。同義詞與個人筆記可自行填寫，翻卡後會顯示。同一詞義放在多分類時共用進度。
5. AI 補充先顯示預覽，確認後才套用；仍需要保存編輯內容。
6. 在設定中建立備份，將 ZIP 自行存到另一個磁碟或備份位置。

多義詞卡片有情境提示；修改例句不會重設進度，不同義項應另建。封存保留歷史；暫停不清空排程。撤銷僅支援當次複習的最後一次評分。

複習頁依序向下顯示詞條、詞義、搭配、同義詞、例句與筆記，操作列固定在底部，不必捲動找評分按鈕；長內容會依可用高度縮放。頁面不顯示來源懸停提示；來源紀錄仍保存在單字資料內。

新增或編輯時可按「← 返回單字庫」離開；未保存的修改會先提示確認。舊版尚未選入的候選詞直接可學，舊版「已略過」的詞顯示於「已暫停」，可按「恢復複習」重新加入。

## Codex 訂閱整合

程式呼叫官方 `codex` CLI，不把 ChatGPT 訂閱當成 OpenAI API 額度。需要在使用這個程式的電腦安裝 Codex CLI 並完成 ChatGPT 登入；現有 Codex 桌面登入能否被 CLI 使用，以設定頁「檢查連線」為準。

```powershell
codex login
codex login status
```

如果找不到 `codex`，在設定中指定 `codex.exe` 完整路徑。模型名稱留空使用 CLI 的預設模型；填入名稱前需確認訂閱可用。CLI 0.153.4 已通過最小生成測試，0.158.0-alpha.2.1 已通過產品 C# 生成路徑實測；其他版本的必要 flags 相容性仍需檢查。

每次生成會使用 Codex 訂閱額度，與你平時的 Codex 工作共用限制。預設每天最多 10 次生成請求，失敗請求可能也消耗額度，因此不自動重試。登入失效、額度不足、沒有網路或沒有 CLI 時，原有詞庫與複習照常運作。程式不購買額度，不讀取／匯出 token，不轉用付費 API。

為生成學習內容，只把目前詞條、詞性、指定詞義及情境送出。程式會停用已知工具／外掛功能、忽略一般使用者設定並要求唯讀；**這不等於一個經證明完全無工具或零檔案讀取能力的通用 API 隔離環境**。不要把不信任的指令當作詞條匯入；管理員強制設定或未來 CLI 變更須重新驗證。偵測到預期外工具事件時會拒絕採用結果，但事後偵測不是事前攔截。

Windows TTS 是另外的離線語音來源，不是 Codex speech API，也不是牛津真人錄音。

## 資料與備份

資料與設定位於 `%LOCALAPPDATA%\WordTrail`，不在 Git repository 或執行檔目錄。程式升級只替換執行檔，不刪此資料夾。測試及示範可用 `--data-dir <獨立目錄>`，勿指向正式資料。

備份包括詞義、例句、分類、學習選擇、排程、複習紀錄與來源。還原採整庫替換，會先驗證並保留舊庫備份；不做兩個資料庫合併。Codex 憑證不在備份中，另一台電腦需要自己登入。

## 專案導覽

| 位置 | 修改什麼 |
| --- | --- |
| `src/WordTrail.Core/Models.cs` | 詞義、學習狀態、服務契約 |
| `src/WordTrail.Desktop` | 畫面、操作與顯示 |
| `src/WordTrail.Infrastructure/SqliteStudyStore.cs` | 資料庫、複習交易、每日配額 |
| `src/WordTrail.Infrastructure/FsrsScheduler.cs` | FSRS 對接與共同參數 |
| `src/WordTrail.Infrastructure/CodexContentGenerator.cs` | 提示、輸出檢查與 CLI 參數 |
| `src/WordTrail.Infrastructure/BackupService.cs` | 一致備份、還原 |
| `content/toeic-starter.json` | 版本化原創起始教材 |
| `tests/WordTrail.Tests` | 有意義的排程／資料／AI 回應測試 |

完整決策與驗收標準見 [執行方案](docs/IMPLEMENTATION_PLAN.md)、[對抗式審查](docs/ADVERSARIAL_REVIEW.md)。實際已通過／未通過項目以 [驗收紀錄](docs/VALIDATION.md) 為準。

目前範圍為單字核心與 AI 補充。短文測驗、聽力理解練習及關聯圖尚未納入，不能將保留資料契約解讀為已實作。
