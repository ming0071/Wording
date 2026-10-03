# 桌面介面導覽

這個 project 只處理 WPF 畫面與操作流程。SQLite、備份、FSRS、Codex 程序與語音的實作在 `Wording.Infrastructure`；資料契約在 `Wording.Core`。

## 從一次操作開始讀

- `App.xaml.cs` 建立資料目錄和空白資料庫並組裝服務；已有資料庫時直接沿用，不自動匯入教材。`--data-dir <路徑>` 可指定獨立測試資料，避免碰到正式單字庫。
- `MainViewModel` 切換五個畫面。編輯頁「返回單字庫」共用導航與未保存修改確認，保留單字庫篩選。`MainWindow.xaml.cs` 只保留需要視窗處理的快捷鍵與關閉提示。
- 新增詞義：`LibraryViewModel.NewCommand` → `EditorViewModel` → `IStudyStore.SaveVocabularyAsync / SaveVocabularyBatchAsync`。詞性使用選單；同詞的新義項以 `SenseEditor` 草稿收集，一個交易保存全部義項，穩定的詞義 ID 避免重試建立重複卡片。同義詞與筆記存在詞義 JSON，舊資料預設為空，無需新增 schema migration。
- 複習：`ReviewViewModel` → `IStudyStore.GetNextReviewAsync / SubmitReviewAsync`。取卡與到期／新詞數量使用已套用的主題範圍；每日新詞配額跨主題共用。資料交易成功才換下一張，評分未確認時不能切換範圍。
- `ReviewKeyboard` 處理翻卡、四個評分、朗讀單字與全部例句的可設定單鍵操作及數字鍵盤別名。輸入欄位、展開中的選單、組合鍵不觸發；長按與暫不可執行的快捷鍵會被消耗，避免焦點按鈕誤執行。快捷鍵設定會檢查衝突，朗讀操作使用尚未占用的鍵。
- AI：`EditorViewModel.GenerateCommand` 只建立預覽；`ApplyPreviewCommand` 改編輯欄位；`SaveCommand` 才寫入資料庫。
- `SettingsViewModel` 保存小型設定與呼叫備份服務。檔案選擇和整庫還原確認在這一處。

## 修改畫面

`Views` 每個 UserControl 對應同名 ViewModel。資料繫結使用完整、可搜尋的屬性名稱；ViewModel 不依賴全域服務查找。淺色 Fluent、常用字級、按鈕、面板與色彩集中在 `App.xaml`。

`App.xaml` 的共用 ComboBox／ComboBoxItem 範本統一圓角、箭頭、展開清單、焦點與選取樣式。`Assets/Wording.svg` 保存品牌向量圖，`scripts/create-app-icon.ps1` 可重建 PNG 與七種解析度的 ICO；專案 `ApplicationIcon` 與 `MainWindow.Icon` 同時設定，以涵蓋檔案總管、視窗與工作列。

新增欄位時，先確定 Core 的資料用途與保存方式，再更新 EditorViewModel 和對應 XAML。不應只加輸入框而沒有資料保存。初始內容來源與個別解釋、搭配、例句來源分開保存；改寫例句會標為使用者內容。

## 非同步操作

`Commands.cs` 的 AsyncCommand 是唯一以 `async void` 接入 ICommand 的位置。它負責禁止同一命令重入、建立取消 token、接住錯誤與復原按鈕狀態。`PageViewModel.Command` 讓同頁資料操作串行，並顯示進度與可讀的錯誤。

導航是 UI 事件邊界，MainViewModel 的兩個非同步導航方法自行接住例外。新增資料操作請使用共用 AsyncCommand，不要另外寫未處理的 `async void`。

評分結果不確定時，ReviewViewModel 保留完整 ReviewSubmission（包括時間與版本），同一評分重試不重新產生 payload。選擇不同評分會提示先重試或重新取卡；離開複習頁會清除當次 Undo。

## UI 驗收

主要互動元件都有穩定 AutomationId，例如 `NavLibrary`、`NewWord`、`HeadwordInput`、`SaveWord`、`VocabularyGrid`、`EnrollWord`、`FlipCard`、`RateGood` 與 `UndoReview`。自動化優先依這些 ID，不依螢幕座標。

一般修改先以 `dotnet test ... --filter` 執行受影響的案例，不要求每輪跑完整套件。`ReviewInteractionTests` 在背景 STA dispatcher 建立真實 WPF 控制項，對 300 張教材檢查 715×560 的複習頁可容納完整詞義、例句與固定操作列，並輸出 `artifacts/layout-check` PNG；不建立視窗，不操控滑鼠／鍵盤，不讀正式資料。`VocabularyAuthoringTests` 檢查上下排版、無來源 ToolTip、同義詞／筆記、詞性選單、多詞義草稿與交易保存。CI 仍保留完整測試。

只有涉及實際 Windows 工作列、DPI、視窗焦點或語音裝置時，再針對該情境驗收。背景版面測試不能證明所有系統 DPI 與原生視窗行為。

`DesktopWorkflowTests` 覆蓋 AI 預覽與保存分離、詞義改變後拒絕舊預覽、來源更新、命令防重入及不確定評分重試；不消耗真實 AI 額度。
