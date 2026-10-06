# Wording Desktop

WPF 畫面與操作流程位於此專案。資料模型與服務契約在 `Wording.Core`；SQLite、備份、FSRS、Codex 程序與語音的實作在 `Wording.Infrastructure`。

[回到主頁](../../README.md#documentation) · [評分與複習排程](../../docs/review-scheduling.md)

## Entry Points

| 操作 | 程式入口 | 行為 |
| --- | --- | --- |
| 啟動 | [App.xaml.cs](App.xaml.cs) | 建立資料目錄、初始化資料庫並組裝服務；同名舊詞條先備份再合併，其餘沿用既有庫 |
| 切換畫面 | [MainViewModel](ViewModels/MainViewModel.cs) | 管理今日學習、單字庫、複習、情境練習、編輯與設定；返回單字庫時保留篩選 |
| 新增／編輯詞義 | [LibraryViewModel](ViewModels/LibraryViewModel.cs) → [EditorViewModel](ViewModels/EditorViewModel.cs) | 以 `SenseEditor` 收集多詞義草稿，再用單一交易保存 |
| 複習 | [ReviewViewModel](ViewModels/ReviewViewModel.cs) | 依已套用的主題取卡；保存評分成功後換下一張 |
| 情境練習 | [PracticeViewModel](ViewModels/PracticeViewModel.cs) | 共用閱讀／聽力、作答解析、單字評分與三個月活動紀錄；導航保留當次文章與答案 |
| 快捷鍵 | [ReviewKeyboard](ReviewKeyboard.cs) | 翻卡、1–4 評分、朗讀單字與全部例句；支援自訂單鍵與數字鍵盤 |
| AI 內容 | [EditorViewModel](ViewModels/EditorViewModel.cs) | `GenerateCommand` 建立預覽，`ApplyPreviewCommand` 套用欄位，`SaveCommand` 保存 |
| 設定與檔案 | [SettingsViewModel](ViewModels/SettingsViewModel.cs) | 保存設定、匯入／匯出 JSON、備份與還原確認 |

`MainWindow.xaml.cs` 處理視窗快捷鍵與關閉提示；未保存修改確認由導航流程共用。使用 `--data-dir <路徑>` 啟動時，可指定獨立的測試資料目錄。

同名詞條保存為一個 `VocabularyItem`，其他解釋存在 `AdditionalSenses`；各組解釋保留內容 ID，共用一個星號與複習卡。初次合併或加入新意思時，先備份再將受影響詞條重設為未學習；一般編輯與相同內容重匯保留進度。複習取卡與卡片數量依主題範圍篩選，每日新詞名額跨主題共用。

情境練習的新詞評分可超過每日上限，仍記入共用的首次學習紀錄；畫面顯示實際數量／設定上限。一般複習頁仍依名額提供未開始的新詞，同一詞條只計一次，未到期與重複評分的保護仍適用。

單字庫搜尋比對詞條名稱與目前保存的各組中文詞義，忽略英文大小寫及搜尋文字首尾空白；搭配、例句及筆記不納入比對。列表中的詞義區塊依整列高度垂直置中，刪除其他解釋後的單組詞義也維持置中。

複習答案以同一個範本呈現所有詞義：單組占滿寬度，每欄至少保留 360 個 WPF 單位，依可用寬度使用一、二或三欄；三組在中等寬度時排列為兩組加下一列一組。維持原本字級，不依高度縮小整張卡；超出高度時只捲動詞義區，詞條、朗讀按鈕與評分固定顯示。各欄保留自己的詞性、提示、解釋、搭配、同義詞、例句與筆記，文字樣式與單組一致；朗讀依各組順序讀出全部英文例句，星號與評分仍屬於整個詞條。換卡或翻卡會回到詞義區頂端。

複習的詞性透過 `PartOfSpeechDisplay` 將既有英文縮寫／名稱轉成中文顯示，原始欄位保持原樣，自訂詞性也保留。並排時使用共用欄位高度，對齊中文詞義、英文解釋與例句；單欄時依各組內容自然排列。提示另有標示，不當作英文解釋。缺少英文解釋時顯示「尚未提供英文解釋」，不自動補寫資料。

翻卡前的詞條、提示、朗讀按鈕與回想提示作為同一組垂直置中；翻卡後，詞條與詞義在能完整容納時一起置中。超出高度時讓詞義區捲動，仍保留頁首與評分操作。單組詞義不顯示額外的回想說明，多組仍顯示組數與一起評分的提示。

Codex 情境練習的模型／速度清單由 CLI 動態提供，閱讀與聽力共用生成、作答與解析流程。提示詞、選字規則與資料保存方式見 [情境練習說明](../../docs/scenario-practice.md)；功能 PNG 展示與更新方式見 [展示素材](../../docs/images/README.md)。

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
| [ReviewInteractionTests.cs](../../tests/Wording.Tests/ReviewInteractionTests.cs) | 快捷鍵、評分提交、起始教材在 715×560 複習頁的版面 |
| [WordGroupingTests.cs](../../tests/Wording.Tests/WordGroupingTests.cs) | 同名詞條合併、單一星號與卡片、多組解釋、備份與進度重設 |
| [VocabularyAuthoringTests.cs](../../tests/Wording.Tests/VocabularyAuthoringTests.cs) | 上下排版、同義詞／筆記、詞性選單、多詞義草稿與交易保存 |
| [DesktopWorkflowTests.cs](../../tests/Wording.Tests/DesktopWorkflowTests.cs) | AI 預覽與保存分離、舊預覽拒絕、來源更新、命令防重入與評分重試 |
| [LearningFlowUpdateTests.cs](../../tests/Wording.Tests/LearningFlowUpdateTests.cs) | 自動朗讀、多主題、學習入口與學習紀錄 |
| [ResponsiveDashboardTests.cs](../../tests/Wording.Tests/ResponsiveDashboardTests.cs) | 今日學習版面與熱力圖月份標籤 |
| [PracticeInteractionTests.cs](../../tests/Wording.Tests/PracticeInteractionTests.cs) | 情境練習最小視窗、作答、聽力逐字稿與翻譯顯示時機 |
| [PracticeReliabilityTests.cs](../../tests/Wording.Tests/PracticeReliabilityTests.cs) | 生成取消與晚回傳、保存衝突、保存邊界與設定往返 |
| [ConfirmationDialogTests.cs](../../tests/Wording.Tests/ConfirmationDialogTests.cs) | 編輯、練習取代與備份還原共用樣式，預設保留目前內容 |
| [ModelPickerTests.cs](../../tests/Wording.Tests/ModelPickerTests.cs) | 動態模型與加速選單、清單更新、保存偏好、CLI 預設與失效選擇提示 |

介面測試在背景 STA dispatcher 建立真實 WPF 控制項；版面截圖輸出至 `artifacts/layout-check`，測試報告輸出至 `artifacts/test-results`。測試使用獨立資料或替身，不開啟正式單字庫，也不消耗真實 AI 額度。CI 執行完整套件。

主要互動元件有穩定的 `AutomationId`，例如 `NavLibrary`、`NewWord`、`HeadwordInput`、`SaveWord`、`VocabularyGrid`、`FlipCard`、`RateGood` 與 `UndoReview`，可用於定位控制項。

涉及 Windows 工作列、DPI、視窗焦點或語音裝置時，需針對實際環境驗收；背景版面測試的範圍是控制項排版與操作邏輯。
