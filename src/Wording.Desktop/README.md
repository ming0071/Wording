# Wording Desktop

WPF 畫面與操作流程位於此專案。資料模型與服務契約在 `Wording.Core`；SQLite、備份、FSRS、Codex 程序與語音的實作在 `Wording.Infrastructure`。

[回到主頁](../../README.md#documentation) · [評分與複習排程](../../docs/review-scheduling.md)

## Entry Points

| 操作 | 程式入口 | 行為 |
| --- | --- | --- |
| 啟動 | [App.xaml.cs](App.xaml.cs) | 建立資料目錄、初始化資料庫並組裝服務；沿用既有庫，教材需自行匯入 |
| 切換畫面 | [MainViewModel](ViewModels/MainViewModel.cs) | 管理今日學習、單字庫、複習、編輯與設定；返回單字庫時保留篩選 |
| 新增／編輯詞義 | [LibraryViewModel](ViewModels/LibraryViewModel.cs) → [EditorViewModel](ViewModels/EditorViewModel.cs) | 以 `SenseEditor` 收集多詞義草稿，再用單一交易保存 |
| 複習 | [ReviewViewModel](ViewModels/ReviewViewModel.cs) | 依已套用的主題取卡；保存評分成功後換下一張 |
| 快捷鍵 | [ReviewKeyboard](ReviewKeyboard.cs) | 翻卡、1–4 評分、朗讀單字與全部例句；支援自訂單鍵與數字鍵盤 |
| AI 內容 | [EditorViewModel](ViewModels/EditorViewModel.cs) | `GenerateCommand` 建立預覽，`ApplyPreviewCommand` 套用欄位，`SaveCommand` 保存 |
| 設定與檔案 | [SettingsViewModel](ViewModels/SettingsViewModel.cs) | 保存設定、匯入／匯出 JSON、備份與還原確認 |

`MainWindow.xaml.cs` 處理視窗快捷鍵與關閉提示；未保存修改確認由導航流程共用。使用 `--data-dir <路徑>` 啟動時，可指定獨立的測試資料目錄。

同一個單字的不同詞義各有穩定 ID，重試保存不會重複建立卡片。同義詞與筆記存在詞義 JSON，舊資料預設為空。複習取卡與卡片數量依主題範圍篩選，每日新詞名額則跨主題共用。

## Styles & Assets

`Views` 中的頁面對應同名 ViewModel。資料繫結使用完整、可搜尋的屬性名稱，服務透過建構式傳入。

[Styles.xaml](Styles.xaml) 集中管理字級、按鈕、面板、色彩與共用 ComboBox／ComboBoxItem 範本；[App.xaml](App.xaml) 載入樣式並啟用淺色 Fluent 主題。

[Assets/Wording.svg](Assets/Wording.svg) 保存品牌向量圖。[create-app-icon.ps1](../../scripts/create-app-icon.ps1) 可重建 PNG 與七種解析度的 ICO；專案 `ApplicationIcon` 與 `MainWindow.Icon` 同時設定，涵蓋檔案總管、視窗與工作列。

新增欄位時，先確定 Core 的資料用途與保存方式，再更新 EditorViewModel 和對應 XAML。初始內容來源與個別解釋、搭配、例句來源分開保存；改寫例句會標為使用者內容。

## Commands & Navigation

[Commands.cs](ViewModels/Commands.cs) 的 `AsyncCommand` 以 `async void` 接入 `ICommand`，負責禁止同一命令重入、建立取消 token、接住錯誤與復原按鈕狀態。`PageViewModel.Command` 讓同頁資料操作依序執行，並顯示進度與錯誤。新增資料操作使用這套共用命令。

MainViewModel 的非同步導航方法自行接住例外。評分未確認保存時不能切換主題範圍；ReviewViewModel 保留完整 `ReviewSubmission`（包括時間與版本），重試同一評分時沿用原內容。選擇不同評分會提示先重試或重新取卡，離開複習頁會清除當次撤銷能力。

快捷鍵在輸入欄位、展開中的選單或使用組合鍵時不觸發；長按與暫不可執行的快捷鍵會被消耗，避免焦點按鈕誤執行。設定會檢查按鍵衝突。

## Tests

從專案根目錄執行受影響的案例，例如桌面操作測試：

```powershell
.\scripts\test.ps1 -Filter 'FullyQualifiedName~DesktopWorkflowTests'
```

`DesktopWorkflowTests` 是分散於多個檔案的 partial class；篩選時使用類別或測試方法名稱，檔名不一定是測試類別名。

| 測試檔案 | 內容 |
| --- | --- |
| [ReviewInteractionTests.cs](../../tests/Wording.Tests/ReviewInteractionTests.cs) | 快捷鍵、評分提交、300 張起始教材在 715×560 複習頁的版面 |
| [VocabularyAuthoringTests.cs](../../tests/Wording.Tests/VocabularyAuthoringTests.cs) | 上下排版、同義詞／筆記、詞性選單、多詞義草稿與交易保存 |
| [DesktopWorkflowTests.cs](../../tests/Wording.Tests/DesktopWorkflowTests.cs) | AI 預覽與保存分離、舊預覽拒絕、來源更新、命令防重入與評分重試 |
| [LearningFlowUpdateTests.cs](../../tests/Wording.Tests/LearningFlowUpdateTests.cs) | 自動朗讀、多主題、學習入口與學習紀錄 |
| [ResponsiveDashboardTests.cs](../../tests/Wording.Tests/ResponsiveDashboardTests.cs) | 今日學習版面與熱力圖月份標籤 |

介面測試在背景 STA dispatcher 建立真實 WPF 控制項；版面截圖輸出至 `artifacts/layout-check`，測試報告輸出至 `artifacts/test-results`。測試使用獨立資料或替身，不開啟正式單字庫，也不消耗真實 AI 額度。CI 執行完整套件。

主要互動元件有穩定的 `AutomationId`，例如 `NavLibrary`、`NewWord`、`HeadwordInput`、`SaveWord`、`VocabularyGrid`、`FlipCard`、`RateGood` 與 `UndoReview`，可用於定位控制項。

涉及 Windows 工作列、DPI、視窗焦點或語音裝置時，需針對實際環境驗收；背景版面測試的範圍是控制項排版與操作邏輯。
