# Wording

個人用的 Windows 英文單字庫。C#／WPF／SQLite，包含逐詞義間隔複習、300 筆原創教材、Windows 英文發音，以及使用既有 ChatGPT 登入的本機 Codex 文字補充。

教材以約 TOEIC 450 的學習者為出發點；不是官方必考詞表，也不提供分數保證。AI 編寫的內容有來源標示，可以修改。

## 直接使用

從 [GitHub Releases](https://github.com/ming0071/Wording/releases) 下載 Windows x64 ZIP，完整解壓後開啟 `Wording.exe`。這是自足程式包，使用時不必安裝 Visual Studio 或 .NET SDK；Codex AI 功能仍需要另外設定 CLI 登入。

自動建置的程式包與測試報告也在 [GitHub Actions](https://github.com/ming0071/Wording/actions)。私人專案的下載需要使用可存取此 repository 的 GitHub 帳號登入。

## 從原始碼開始使用

- Windows 11 x64、Git、.NET 10 SDK（只有 .NET Runtime 不足以編譯或執行測試）。
- Visual Studio 2026 18.0 或更新版本，安裝「.NET 桌面開發」工作負載；或單獨安裝 .NET 10 SDK，使用命令列建置。
- Visual Studio 2022 不能直接作為本專案 .NET 10 的支援 IDE。SDK 與 Visual Studio 是不同的工具。
- 內嵌線上字典需要 Microsoft Edge WebView2 Runtime；若無法載入，可安裝 Runtime 或使用外部開啟入口。

在 PowerShell 執行下列指令。私人 repository 需要有權限的 GitHub 帳號；第一次建置需要網路下載 NuGet 套件。

```powershell
git clone https://github.com/ming0071/Wording.git
cd Wording
.\scripts\build.ps1
.\src\Wording.Desktop\bin\Release\net10.0-windows\Wording.exe
```

`build.ps1` 會還原套件、編譯並執行全部測試，成功後即可開啟上面的 EXE。後續啟動直接開 EXE，不必每次編譯。編譯前請關閉這個目錄中的 Wording，以免 Windows 鎖住檔案。

也可在 Visual Studio 開啟 `Wording.sln`，將 `Wording.Desktop` 設為啟始專案。SDK 允許 .NET 10 穩定服務更新；實際使用版本會出現在 CI 紀錄。腳本會先尋找 PATH 中的 `dotnet`，再尋找 `C:\Program Files\dotnet\dotnet.exe`。如果剛安裝 SDK 後直接執行 `dotnet` 找不到指令，重新開啟 PowerShell。

若 PowerShell 阻擋 `.ps1`，可僅在此次程序中執行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

## 日常使用

1. 在「今日學習」按「開始今天的複習」。單字庫中的未學新詞自動可用，不必逐字選入；複習範圍可按選一個或多個主題，或按「全部單字庫」，選擇後立即切換。
2. 先處理範圍內的到期卡，再學新詞；預設每日最多 5 個，可在「設定與備份」填入任意非負整數，沒有固定數量上限（0 表示只複習舊詞）。當日初始到期量 10–19 張時仍最多 2 個新詞，20 張以上先不加新詞。每日名額由所有主題共用，切換主題不會重設。
3. 先想答案，再按空白鍵翻卡，按 1／2／3／4 選「重來／困難／良好／簡單」，S 朗讀單字，E 朗讀全部英文例句；也可用滑鼠，數字鍵盤同樣可用。所有按鍵可在「設定與備份」自訂。只有自評提交會更新排程。
4. 新增時用上方按鈕選詞性，可按「新增另一組解釋」輸入同一詞條的多個意思，一次保存；每個詞義都有自己的進度。同義詞與個人筆記可自行填寫，翻卡後會顯示。主題可從選單挑選或輸入新名稱後加入；同一詞義放在多分類時共用進度。
5. AI 補充先顯示預覽，確認後才套用；仍需要保存編輯內容。
6. 在設定中建立備份，將 ZIP 自行存到另一個磁碟或備份位置。

單字庫與複習頁的 ☆／★ 可切換詞義收藏。單字庫「詞義收藏」右上可按建立時間、A–Z／Z–A、熟練程度或星號排序；熟練程度依記憶模型的穩定度，未學詞為 0。舊詞沒有建立日期時沿用原始加入順序，新詞開始記錄建立時間。

「今日學習」熱力圖顯示近一年每天複習的詞義數；同一詞義每天只計一次，已撤銷的評分不列入。預設開卡自動讀單字、翻卡自動讀英文例句，可在設定關閉。

新增頁右側可切換線上字典與 Codex。線上字典僅供查閱，不會自動填入；網站可能要求驗證或限制內嵌載入，可改用外部開啟。Codex 預覽需按套用才填入，最後仍須保存。

多義詞卡片有情境提示；修改例句不會重設進度，不同義項應另建。封存保留歷史；暫停不清空排程。撤銷僅支援當次複習的最後一次評分。

複習頁依序向下顯示詞條、詞義、搭配、同義詞、例句與筆記，操作列固定在底部，不必捲動找評分按鈕；長內容會依可用高度縮放。頁面不顯示來源懸停提示；來源紀錄仍保存在單字資料內。

新增或編輯時可按「← 返回單字庫」離開；未保存的修改會先提示確認。舊版尚未選入的候選詞直接可學，舊版「已略過」的詞顯示於「已暫停」，可按「恢復複習」重新加入。

## Codex 訂閱整合

程式呼叫官方 `codex` CLI，不把 ChatGPT 訂閱當成 OpenAI API 額度。需要在使用這個程式的電腦安裝 Codex CLI 並完成 ChatGPT 登入；現有 Codex 桌面登入能否被 CLI 使用，以設定頁「檢查連線」為準。

```powershell
codex login
codex login status
```

如果找不到 `codex`，在設定中使用自動尋找或瀏覽指定 `codex.exe` 完整路徑，並按「檢查連線」。模型名稱留空使用 CLI 的預設模型；填入名稱前需確認訂閱可用。CLI 升級後請重新檢查連線與單次生成，歷史驗證不保證所有版本相容。

每次生成會使用 Codex 訂閱額度，與你平時的 Codex 工作共用限制。預設每天最多 10 次生成請求，失敗請求可能也消耗額度，因此不自動重試。登入失效、額度不足、沒有網路或沒有 CLI 時，原有詞庫與複習照常運作。程式不購買額度，不讀取／匯出 token，不轉用付費 API。

為生成學習內容，只把目前詞條、詞性、指定詞義及情境送出。程式會停用已知工具／外掛功能、忽略一般使用者設定並要求唯讀；**這不等於一個經證明完全無工具或零檔案讀取能力的通用 API 隔離環境**。不要把不信任的指令當作詞條匯入；管理員強制設定或未來 CLI 變更須重新驗證。偵測到預期外工具事件時會拒絕採用結果，但事後偵測不是事前攔截。

Windows TTS 是另外的離線語音來源，不是 Codex speech API，也不是牛津真人錄音。

## 資料與備份

資料與設定位於 `%LOCALAPPDATA%\Wording`，不在 Git repository 或執行檔目錄。程式升級只替換執行檔，不刪此資料夾。測試及示範可用 `--data-dir <獨立目錄>`，勿指向正式資料。

Wording 原名 WordTrail。升級時若已有 `%LOCALAPPDATA%\WordTrail\wordtrail.db`，會直接沿用該資料庫及同目錄設定，不搬移或清空資料；舊版備份仍可還原。自訂 `--data-dir` 內的既有 `wordtrail.db` 也可沿用。新安裝才使用 `Wording\wording.db`。既有本機專案資料夾叫 `WordTrail` 也不影響操作；新 clone 的預設資料夾名稱是 `Wording`。

- `wording.db`：SQLite 單字、分類、星號、排程與複習紀錄。
- `settings.json`：新詞數量、快捷鍵、朗讀與 Codex 設定。
- `.db` 是資料庫，不可當文字檔編輯；關閉 App 後再進行資料庫檔案操作。

可以用 JSON 匯入／匯出維護詞彙、讓 AI 擴充內容，保留既有詞義 ID 即可保留進度。App 的設定頁有入口；命令列操作詳見 [檔案維護說明](docs/vocabulary-files.md)。JSON 只包含詞彙內容，完整進度與星號請使用整庫備份。

備份包括詞義、例句、分類、學習選擇、排程、複習紀錄與來源。還原採整庫替換，會先驗證並保留舊庫備份；不做兩個資料庫合併。Codex 憑證不在備份中，另一台電腦需要自己登入。

## 執行測試

在專案根目錄執行全部自動測試：

```powershell
.\scripts\test.ps1
```

腳本會還原套件、編譯並跑 xUnit 測試，成功時顯示通過數量，失敗時列出案例及原因並回傳失敗狀態。測試執行檔放在 `bin/TestRun/Release`，與日常使用的 App 分開，可避免檔案鎖定。資料測試使用獨立暫存資料庫，不會修改 `%LOCALAPPDATA%\Wording`；介面測試在背景載入 WPF，不需要畫面操控權。

完整報告在 `artifacts/test-results/tests.trx`，篩選測試另存 `tests-filtered.trx`，同類報告每次覆寫；部分介面測試產生的圖片在 `artifacts/layout-check`。這些本機產物不提交 Git。報告可用 Visual Studio 開啟，或查看 PowerShell 顯示的失敗訊息。

若只檢查這次改動，可用案例名稱或測試類別篩選，例如：

```powershell
# 星號保存與排序
.\scripts\test.ps1 -Filter 'FullyQualifiedName~StarsPersist|FullyQualifiedName~LibrarySortsBy'

# 資料庫相關案例
.\scripts\test.ps1 -Filter 'FullyQualifiedName~SqliteStudyStoreTests'
```

不使用腳本的等效指令（需 `dotnet` 在 PATH 中）：

```powershell
dotnet test tests/Wording.Tests/Wording.Tests.csproj -c Release -p:OutputPath=bin/TestRun/Release/ --logger "trx;LogFileName=tests.trx" --results-directory artifacts/test-results
```

自動測試涵蓋資料保存、複習排程、評分／撤銷、備份還原、AI 回應解析、快捷鍵邏輯、星號、排序及部分版面。它不會呼叫真實 Codex 生成或實際朗讀；通過不代表真實網路服務與所有 Windows 顯示環境已驗收。

### 人工試用

以獨立資料目錄啟動，可試用功能而不影響正式單字庫。重複使用同一路徑會保留測試紀錄。

```powershell
.\src\Wording.Desktop\bin\Release\net10.0-windows\Wording.exe --data-dir "$PWD\.local\manual-test"
```

依序檢查新增多個詞義、保存後重開、未保存返回提示、今日學習進入複習、空白鍵與數字評分、S／E 朗讀、星號與各項排序、最大化／縮小版面，以及備份還原。需要 AI 時另測 Codex 連線、生成、預覽、套用與保存；這會使用訂閱額度。

## 更新與維護

1. 先在 App 建立整庫備份，關閉 Wording。
2. 若改過原始碼，先檢查 `git status` 並保存自己的變更，再更新；Git 不會替你備份單字庫。
3. 在專案根目錄執行更新與建置，成功後重新開啟 EXE：

```powershell
git pull --ff-only
.\scripts\build.ps1
.\src\Wording.Desktop\bin\Release\net10.0-windows\Wording.exe
```

日常小改動先跑相關測試；版本驗收再跑全部。若出現檔案使用中，關閉正在使用同一編譯目錄的 App，或使用 `test.ps1` 的獨立測試輸出。若測試失敗，保留終端錯誤和 TRX，不要把失敗結果當成可發布版本。

要製作可給別台 Windows 電腦使用的自足 ZIP（不需 .NET SDK），執行：

```powershell
.\scripts\publish.ps1 -Version 0.1.9
```

輸出為 `artifacts/Wording-0.1.9-win-x64`、同名 ZIP 與 `.sha256`。發布腳本只打包，不會執行測試；請先驗證。GitHub Actions 在 push／PR 時執行教材驗證、建置、全部測試與打包；教材驗證另使用 Python 3.12，本機一般使用與 xUnit 測試不需要 Python。

## 專案導覽

| 位置 | 修改什麼 |
| --- | --- |
| `src/Wording.Core/Models.cs` | 詞義、學習狀態、服務契約 |
| `src/Wording.Desktop` | 畫面、操作與顯示 |
| `src/Wording.Infrastructure/SqliteStudyStore.cs` | 資料庫、複習交易、每日配額 |
| `src/Wording.Infrastructure/FsrsScheduler.cs` | FSRS 對接與共同參數 |
| `src/Wording.Infrastructure/CodexContentGenerator.cs` | 提示、輸出檢查與 CLI 參數 |
| `src/Wording.Infrastructure/BackupService.cs` | 一致備份、還原 |
| `content/toeic-starter.json` | 版本化原創起始教材 |
| `tests/Wording.Tests` | 有意義的排程／資料／AI 回應測試 |

完整決策與驗收標準見 [執行方案](docs/IMPLEMENTATION_PLAN.md)、[對抗式審查](docs/ADVERSARIAL_REVIEW.md)。實際已通過／未通過項目以 [驗收紀錄](docs/VALIDATION.md) 為準。

目前範圍為單字核心與 AI 補充。短文測驗、聽力理解練習及關聯圖尚未納入，不能將保留資料契約解讀為已實作。
