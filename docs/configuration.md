# Configuration

程式預設值、個人設定與建置版本各有一個來源。調整數量或文章範圍時，畫面文字、生成提示與資料層會共用相同的設定。

[回到主頁](../README.md#documentation) · [評分與複習排程](review-scheduling.md) · [情境練習](scenario-practice.md)

## Sources

| 來源 | 用途 | 套用方式 |
| --- | --- | --- |
| [app-defaults.json](../src/Wording.Core/Configuration/app-defaults.json) | 每日新詞、快捷鍵、朗讀、AI 次數與逾時、文章字數、選字權重及練習紀錄期間 | 開發時修改後重新建置；發行包同時附上 `Configuration/app-defaults.json`，修改發行包內這份檔案後重新啟動 |
| `%LOCALAPPDATA%\Wording\settings.json` | 使用者已選擇的新詞上限、快捷鍵、AI 設定與閱讀／聽力選項 | 優先於程式預設；透過「設定與備份」或練習選項保存 |
| [Directory.Build.props](../Directory.Build.props) | App 版本與共同建置設定 | 建置時產生組件版本；側欄、打包檔名、版本 tag 驗證與 Release 共用 |
| [Styles.xaml](../src/Wording.Desktop/Styles.xaml) | 共用色彩、字型與控制項樣式 | 修改後重新建置 |

`app-defaults.json` 每次啟動只讀取一次，不會寫入個人資料目錄。必填欄位、未知欄位、錯誤型別、快捷鍵衝突與數值範圍都會驗證；無效設定會顯示錯誤，不會自動清空資料庫。打包檢查確認這份檔案存在且包含有效 JSON。

新增預設值不會覆蓋已保存的選擇。每日新詞新預設為 **25 個**，到期複習另計；已保存 5 個等自訂數量時維持原值，可在「設定與備份」改為 25。

## Values

| 區塊 | 可調整的值 |
| --- | --- |
| `Review` | 新詞預設、複習頁使用的繁體中文（台灣）輸入法、到期卡減量／暫停新詞門檻及減量名額 |
| `Shortcuts`、`Speech` | 翻卡、四種評分與朗讀快捷鍵，自動朗讀單字／例句 |
| `Ai` | 執行檔、模型與加速模式預設、每日預設次數與最大次數、單字生成／練習生成／登入狀態檢查／模型清單查詢逾時 |
| `Practice.Defaults` | 閱讀／聽力、文章類型、難度、長度、單字量、題數與語速預設 |
| `Practice.Lengths` | 短／中／長篇英文字數範圍與基礎目標詞數；單字量每級增加 `DensityWordIncrement` 個目標詞 |
| `Practice` 其他欄位 | 主題抽樣回顧天數、近幾篇降權、星號權重、熟練詞權重、近期曝光恢復時間與最低權重 |
| `Practice.HistoryMonths` | 完成紀錄保存與主題足跡顯示期間，預設 3 個月；縮短會在下一次讀取時清除較舊紀錄 |

同一天已建立的新詞減量名額仍保持當天的規則；設定的新詞上限可調整，減量門檻的新值會在下一天建立名額時套用。

FSRS 公式與固定參數、資料庫 schema 版本、備份／教材格式版本、題目四選一結構及資料安全限制保留為程式規格。這些值的變更需要對應的排程驗證或資料遷移，不能當作一般偏好設定修改。套件版本保留在各專案的 `PackageReference`，實際還原版本由 `packages.lock.json` 鎖定。

## Codex Models

「設定與備份」的生成模型改用選單，每次開啟頁面時重新查詢，也可以按「更新模型清單」。第一個選項「使用 CLI 預設」以空字串保存，生成時不傳入固定模型名稱，跟隨目前 CLI 的預設。選擇其他模型時保存 CLI 回傳的模型識別碼，單字生成與閱讀／聽力共用此設定；更新清單本身不會保存偏好。

清單來自官方 [app-server `model/list`](https://learn.chatgpt.com/docs/app-server#list-models-modellist)，完成初始化與 ChatGPT 登入狀態檢查後，逐頁查詢可顯示的文字模型。支援新增模型及新增回應欄位，程式不內建特定模型名稱。查詢使用本機標準輸入／輸出，不開啟網路監聽埠、不建立對話、不生成內容，也不占每日生成次數。登入由 CLI 管理，程式不直接讀取憑證。

切換 ChatGPT 帳號後請更新清單。Codex 自己可能使用快取或內建 catalog；官方也說明了這個 [清單新鮮度限制](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)。更新 CLI 後重新查詢可取得該版本回傳的清單，不能保證模型一發布就立即出現。路徑填入 `codex` 可自動尋找目前安裝的版本；指定舊版本的完整路徑則仍使用那個執行檔。

若上次選擇未出現在新清單，畫面保留並標示它，保存前須改選其他模型或 CLI 預設。查詢逾時、未登入或舊 CLI 不支援此介面時，原設定保留，仍可選 CLI 預設並使用離線學習。查詢可取消；取回舊路徑的晚到回應不會覆蓋新路徑清單。最終的模型權限與可用性仍由生成服務判斷。

## Codex Speed

生成速度預設為「標準」，`Ai.ServiceTier` 與個人設定 `CodexServiceTier` 用空字串代表標準；生成時明確傳入 `service_tier="default"`。單字補充、閱讀與聽力共用保存的選擇，不受 CLI 個人加速設定影響。

其他選項來自所選模型的 `model/list.serviceTiers`，保存原始 `id` 並顯示 `name` 與 `description`。使用 CLI 預設模型時，顯示清單中 `isDefault` 模型提供的模式。較舊 CLI 只提供 `additionalSpeedTiers` 時支援其 Fast 選項；沒有提供模式時只顯示標準，不自行推測模型支援加速。未來清單新增模式可直接顯示，沒有固定的模型或倍速清單。

切換模型或更新清單後，原選擇如果不再可用會保留提示，保存前須改選可用模式或標準。清單讀取失敗時不自動改掉已保存的選擇；生成服務仍會檢查實際權限。CLI 生成以 `--enable fast_mode` 啟用模式設定，保留 ChatGPT 登入及原有工具限制。

[官方速度說明](https://learn.chatgpt.com/docs/agent-configuration/speed)指出 Fast 會消耗較多額度。速度依模型、方案與推出狀態而異；本機清單可能標示 1.5x、2x 等資訊，但不代表整份測驗的完成時間保證縮短相同倍數。Ultrafast 等模式只有 CLI 實際提供時才會出現在選單。相關參數見[設定參考](https://learn.chatgpt.com/docs/config-file/config-reference)。

## Review Keyboard

進入複習頁時，先檢查是否安裝繁體中文（台灣）輸入法；有安裝時使用它的英文模式，不切換到英文（美國）鍵盤。沒有安裝時不設定輸入語言或中英模式。`Review.PreferredInputLanguage` 固定為 `zh-TW`，不提供其他語言的後備選擇。離開複習焦點後恢復原輸入語言；中英模式由輸入法本身管理。

輸入語言範圍使用 WPF 官方的 [InputLanguageManager](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.inputlanguagemanager?view=windowsdesktop-10.0)，英文模式使用 [PreferredImeState](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.inputmethod.preferredimestate?view=windowsdesktop-10.0) 與 [PreferredImeConversionMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.input.inputmethod.preferredimeconversionmode?view=windowsdesktop-10.0)。實際 Windows 輸入法指示與組字行為需在使用者已安裝的輸入法上驗收。
