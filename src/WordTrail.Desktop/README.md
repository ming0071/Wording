# 桌面介面導覽

這個 project 只處理 WPF 畫面與操作流程。SQLite、備份、FSRS、Codex 程序與語音的實作在 `WordTrail.Infrastructure`；資料契約在 `WordTrail.Core`。

## 從一次操作開始讀

- `App.xaml.cs` 建立資料目錄、匯入候選教材並組裝服務；`--data-dir <路徑>` 可指定獨立測試資料，避免碰到正式單字庫。
- `MainViewModel` 切換五個畫面。`MainWindow.xaml.cs` 只保留需要視窗處理的快捷鍵與關閉提示。
- 新增詞義：`LibraryViewModel.NewCommand` → `EditorViewModel` → `IStudyStore.SaveVocabularyAsync`。
- 複習：`ReviewViewModel` → `IStudyStore.GetNextReviewAsync / SubmitReviewAsync`。資料交易成功才換下一張。
- AI：`EditorViewModel.GenerateCommand` 只建立預覽；`ApplyPreviewCommand` 改編輯欄位；`SaveCommand` 才寫入資料庫。
- `SettingsViewModel` 保存小型設定與呼叫備份服務。檔案選擇和整庫還原確認在這一處。

## 修改畫面

`Views` 每個 UserControl 對應同名 ViewModel。資料繫結使用完整、可搜尋的屬性名稱；ViewModel 不依賴全域服務查找。淺色 Fluent、常用字級、按鈕、面板與色彩集中在 `App.xaml`。

新增欄位時，先確定 Core 的資料用途與保存方式，再更新 EditorViewModel 和對應 XAML。不應只加輸入框而沒有資料保存。初始內容來源與個別解釋、搭配、例句來源分開保存；改寫例句會標為使用者內容。

## 非同步操作

`Commands.cs` 的 AsyncCommand 是唯一以 `async void` 接入 ICommand 的位置。它負責禁止同一命令重入、建立取消 token、接住錯誤與復原按鈕狀態。`PageViewModel.Command` 讓同頁資料操作串行，並顯示進度與可讀的錯誤。

導航是 UI 事件邊界，MainViewModel 的兩個非同步導航方法自行接住例外。新增資料操作請使用共用 AsyncCommand，不要另外寫未處理的 `async void`。

評分結果不確定時，ReviewViewModel 保留完整 ReviewSubmission（包括時間與版本），同一評分重試不重新產生 payload。選擇不同評分會提示先重試或重新取卡；離開複習頁會清除當次 Undo。

## UI 驗收

主要互動元件都有穩定 AutomationId，例如 `NavLibrary`、`NewWord`、`HeadwordInput`、`SaveWord`、`VocabularyGrid`、`EnrollWord`、`FlipCard`、`RateGood` 與 `UndoReview`。自動化優先依這些 ID，不依螢幕座標。

必須另用實際發布包測試：新增詞義 → 分類 → 搜尋 → 選入 → 翻面 → 評分 → 撤銷 → 重啟保存，以及備份／還原與 AI 成功／失敗。檢查 1000×700 以上視窗、100%／150% DPI、長例句、空清單、捲動與 Tab 焦點。XML 格式檢查和 ViewModel 測試不能取代真實 WPF 視窗驗收。

`DesktopWorkflowTests` 覆蓋 AI 預覽與保存分離、詞義改變後拒絕舊預覽、來源更新、命令防重入及不確定評分重試；不消耗真實 AI 額度。
