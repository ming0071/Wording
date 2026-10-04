# Quality & Maintenance

功能完成後的維護重點是保護既有資料、驗證操作邊界，並確認實際下載的程式包可用。

[回到主頁](../README.md#documentation) · [桌面介面導覽](../src/Wording.Desktop/README.md)

## Automated Checks

```powershell
python -m unittest discover -s scripts/tests -v
python scripts/validate-content.py
.\scripts\build.ps1 -LockedRestore
.\scripts\publish.ps1 -LockedRestore
python scripts/verify-package.py
```

Python 僅用於開發驗證，Windows App 不需要 Python。`validate-content.py` 檢查原創教材的 297 個詞條／300 組解釋；完整 .NET 測試另驗證完整詞庫的 1,113 個詞條／1,385 組解釋、動詞片語補充檔的 99 個詞條／102 組解釋，以及 JSON 格式範例的匯入。

| 檢查 | 保護的情境 |
| --- | --- |
| [練習可靠性](../tests/Wording.Tests/PracticeReliabilityTests.cs) | 取消後晚回傳不能覆蓋答案、重按不會重複生成、取消後能重新操作、相同 ID 的不同完成紀錄不能覆寫、三個月保存邊界、無效設定不能覆蓋舊檔 |
| [選用教材](../tests/Wording.Tests/OptionalVocabularyTests.cs) | 四份教材／範例能匯入；補充片語時保留既有情境分類、星號及其他單字，新增另一組意思的詞條重設為未學習，其餘排程保留；重複匯入保留 ID 與學習進度；作者的學習紀錄不會帶入 |
| [同詞多義](../tests/Wording.Tests/WordGroupingTests.cs) | 同名資料真正合併為一個詞條／星號／複習卡，合併前備份、失敗完整回退、受影響詞條重設為未學習、巢狀解釋可編輯與複習、舊 JSON 重匯不再新增重複資料 |
| [卡片筆記清理](../tests/Wording.Tests/VocabularyNotesTests.cs) | 只清除教材重複聲明、保留用法與個人筆記；先備份、失敗回退、原卡片／星號／排程保留，舊檔重匯不帶回聲明 |
| [設定與輸入法](../tests/Wording.Tests/ApplicationConfigurationTests.cs) | 新預設與已保存偏好分開、JSON 格式與範圍檢查、減量門檻邊界、英文鍵盤選擇與複習頁 IME 範圍 |
| [Codex 模型清單](../tests/Wording.Tests/CodexModelCatalogTests.cs)、[模型選單](../tests/Wording.Tests/ModelPickerTests.cs) | 初始化順序、分頁、新模型與未知欄位、隱藏／非文字模型、ChatGPT 登入、錯誤輸出遮蔽、查詢失敗、舊路徑晚到回應及選擇保存 |
| [發行包](../scripts/verify-package.py) | SHA-256、必要檔案、Windows x64 EXE、.NET／WPF 自包含 runtime、三個專案版本、提示詞、授權；排除重複路徑、使用者資料與暫存檔 |
| [發行驗證器測試](../scripts/tests/test_verify_package.py) | 缺提示詞／授權、錯誤校驗碼、錯版本、非自包含包、錯誤 EXE、非法路徑、重複項目、損毀 ZIP 都應失敗 |

版本以 `Directory.Build.props` 為準，側欄由組件版本顯示，打包腳本預設讀取同一版本。共同的 `RuntimeIdentifiers` 同時涵蓋一般建置與 win-x64 發行，避免兩種還原改寫不同的套件鎖定檔。更新版本或套件後先正常還原並提交新的 lock files，再以 `-LockedRestore` 驗證。

CI 使用固定 SHA 的官方 Actions、一般建置的唯讀權限與 NuGet 快取。分支上的重複工作會取消，版本 tag 的工作則完整執行。ZIP 與驗證證據保留 14 天；已壓縮的 ZIP 不再重複壓縮。建置、測試與打包之後會驗證套件鎖定檔沒有變動，並在 Actions summary 顯示測試與發行包結果。

版本 tag 推送後，獨立 Release 工作取得同一次驗證的三個固定附件，以草稿上傳並核對 SHA-256，再公開。只有 Release 工作擁有寫入權限，已發布版本不會被覆寫。使用方式見 [版本發布](releases/README.md)，失敗與重試情境由 [發布器測試](../scripts/tests/test_release.py) 驗證。

Actions 版本與行為依官方文件核對：[checkout](https://github.com/actions/checkout)，[setup-dotnet](https://github.com/actions/setup-dotnet)，[setup-python](https://github.com/actions/setup-python)，[upload-artifact](https://github.com/actions/upload-artifact)。

## Historical Files

2026-10-04 檢查：目前沒有找到可直接刪除的已追蹤歷史檔案。

| 檔案／目錄 | 判斷 |
| --- | --- |
| `content/toeic-starter.json` | 仍供手動匯入、教材結構檢查與複習版面測試使用；保留 |
| `tests/Wording.Tests/Fixtures/fsrs-reference.json`、`scripts/generate_fsrs_fixtures.py` | 獨立排程基準與可重建方法；保留 |
| `tests/Wording.Tests/Fixtures/practice-reading-default.json` | 真實生成格式的回歸案例，防止再次拒絕可用題目；保留 |
| 舊品牌相容邏輯 | 僅供作者使用，已移除舊資料路徑與備份品牌辨識。兩份教材皆有固定 ID，自動匯入 ID 使用 Wording 前綴；資料庫結構升級仍保留。資料搬移需透過 App 備份／還原並在實際畫面驗收 |
| `Assets/Wording.svg`、`scripts/create-app-icon.ps1` | 品牌圖示原始檔與重建工具；保留 |
| `docs/images/wording-*.png`、授權文字 | 主 README 與展示索引仍引用；保留。舊操作 GIF 已由清晰 PNG 取代並移除 |
| `artifacts/Wording-0.2.0-win-x64`、舊 ZIP、`artifacts/practice-smoke` 與恢復診斷副本 | 未追蹤的舊打包與診斷輸出，本輪清理；完整資料恢復 ZIP 保留 |
| `bin`、`obj`、`artifacts/layout-check`、`artifacts/test-results` | 可重建的輸出；近期結果有助除錯，清理前先決定需保留哪些證據 |

## Data Migration Checks

更改資料目錄或移除相容路徑前，先保存可還原的完整備份。JSON 不包含複習排程；搬移學習資料時使用 App 的 ZIP 備份／還原功能，原資料庫需保留至驗收完成。

驗收必須包含使用者實際開啟的執行檔與畫面：核對「設定與備份」的資料目錄、單字總數、分類與複習進度。僅在工具環境核對檔案雜湊或資料庫筆數，無法證明使用者的程式已讀取同一份檔案。

`diagnostics.log` 記錄啟動路徑、檔案大小與畫面載入筆數，供讀取結果不一致時對照；不記錄單字內容或測驗答案。診斷寫入失敗不會阻擋正常操作。

## Follow-up Options

2026-10-04 已完成 Release 自動發布、SQLite 資料層拆分、通用確認視窗與歷史輸出清理。SQLite 分為共用連線與 SQL 工具、結構升級、詞庫讀寫、排程與每日名額，既有練習資料層仍獨立。所有 partial 檔案共享同一維護鎖與交易介面，沒有更改 schema 或 FSRS 規則。

確認視窗改為 `ConfirmationDialog` 與明確的內容設定，編輯、取代練習與備份還原都使用相同樣式；Enter／Escape 的預設行為保留目前內容。

以下尚未實作，應依需求決定：

| 優先順序 | 項目 | 理由與取捨 |
| --- | --- | --- |
| 中 | 實際 Windows 驗收 | 125%／150% DPI、視窗焦點、WebView2 安裝狀態與 Windows 英文語音，無法由背景 WPF 測試完全涵蓋 |
| 中 | 測試覆蓋率與弱點／依賴更新檢查 | 測試數量不代表覆蓋率；先量測服務與資料層，再決定合理目標。依賴自動升級需審查 FSRS 行為及 UI 差異 |
| 先量測 | 大詞庫效能 | 目前練習會載入候選詞與歷史後抽樣；應先量測 1 萬以上詞義的載入、篩選與 UI 更新，再決定查詢或快取調整 |

自動測試使用獨立資料庫、合成生成器與 ZIP，沒有操作正式詞庫或呼叫真實 AI。這些檢查不代表英文內容品質、實際語音裝置或高 DPI 視窗已完成驗收。
