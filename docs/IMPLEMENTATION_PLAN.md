# WordTrail：個人英文學習程式執行方案

更新：2026-09-26。這是經多 agent 對抗式審查修正的實作規格；本文件存在不代表應用程式已完成。

## 1. 目標、範圍與目前決策

目標使用者只有一人，使用 Windows 桌面，TOEIC 約 450（L230／R220），期望朝 600–700 前進，每日投入 10–15 分鐘。介面繁體中文、教材英文，程式以可讀的 C# 為主。優先做好可靠且每天願意使用的學習流程，不以功能數或商業化外觀作完成指標。

第一版預設交付以下完整流程；每日短文是否提早納入，仍待使用者回覆已有的範圍問題。未選擇擴大時依核心版執行，不把擴充項目算成已完成。

| 第一版必交付 | 明確行為 |
| --- | --- |
| 單字庫 | 自訂分類、搜尋、新增／編輯單字與詞義、常用片語、封存、同詞義多分類 |
| 起始教材 | 300 個不重複的詞義／固定片語，附繁中解釋、搭配、原創雙語例句、來源資訊 |
| 選詞 | 候選庫與學習池分開；可每批約 20 詞篩選、跳過、暫停後續做 |
| 複習 | FSRS、翻卡、忘記／困難／記得／簡單、暫停、當次最後一次評分撤銷 |
| 發音 | 可用的 Windows 英文 TTS，能停止與重播，標明合成語音 |
| AI 補充 | 優先本機 Codex 訂閱：產生指定詞義的解釋、搭配、兩個雙語例句，預覽後才保存 |
| 可信查閱 | Oxford／Merriam-Webster 等外部辭典查閱入口；查閱連結與內容来源分開 |
| 資料安全 | 本機保存、版本遷移、備份／整庫還原；重啟、更新應用程式均保留進度 |
| 交付 | 原始碼、測試、繁中文件、Windows x64 自足 ZIP、Git 紀錄與 GitHub 推送 |

已確定：使用者於 2026-09-26 選擇「優先驗證本機 Codex 訂閱整合」。不將 ChatGPT 訂閱當成 OpenAI API key；沒有 API 付費授權，也不自動改走付費 API。

後續版本：每日短文及選擇題、聽力理解活動、錯誤類型分析、單字關聯圖。首版不做手機、多人、登入系統、雲端同步、背景提醒、完整模擬考或複雜圖譜平台。

## 2. 使用流程與畫面

只設五個主要畫面：今日學習、單字庫、複習、單字編輯、設定／備份。採 .NET WPF 原生 Fluent、單一強調色、清楚字級與留白；先完成淺色主題。不加入沒有功能需要的動畫或裝飾。

首次啟動建立資料庫並匯入有版本的候選資料包；使用者可立即選少量單字開始，不必先答完 300 詞問卷。不登入 AI 仍可新增、編輯及複習。初次 AI 使用顯示所選內容會交給 Codex，且消耗其訂閱額度。

今日畫面分開顯示「已學且到期」「可開始的新詞」「稍後到期的學習卡」，不把候選詞當到期。以本機日曆日計算新詞配額、UTC 計算精確到期時間。每天預設最多 5 個新詞，使用者可填任意非負整數；當日第一次學習取到期量作簡單降載：少於 10 張依使用者設定、10–19 張最多 2 個、20 張以上為 0 個。這是產品預設，不宣稱為研究定律。

已開始的新詞數當日跨重啟累計，Undo 不反覆製造新名額。先處理到期卡，再提供新詞；每次出卡重新確認最新排程。分鐘級重學到期前顯示等待時間，不提前偽裝成到期。使用者隨時可停止，未完成的到期卡保留。

複習正面顯示詞條、詞性；同詞多義加簡短英文情境提示，避免不知道正在考哪一義。翻面後顯示指定繁中釋義、搭配、例句、來源與四個評分。播放發音、翻面、瀏覽都不更新排程。快捷鍵：Space 翻面，答案顯示後 1–4 評分；輸入框取得焦點時不攔截文字輸入。

新增流程：輸入詞條→手動寫明詞性與詞義，或請 AI 提供少量候選詞義→選定詞義→生成解釋／例句→預覽調整→存入分類。AI 候選不自動變成正式資料；取消不留半成品。已有詞義補充內容時不自動覆蓋使用者筆記。

編輯文字勘誤或例句不改變卡片進度；不同義項必須新增 SenseId。詞義封存保留歷史；刪分類僅移除分類關係。第一版不提供破壞性清空所有進度的快捷操作。

視覺驗收以真實 WPF 畫面為準：首頁、單字庫、翻卡兩面、編輯器、設定，另測空清單、長例句、載入、錯誤狀態。最低支援 1000×700 邏輯像素，測試 100% 與 150% 等效縮放；需記錄是否為真實 DPI 或離屏版面測試，兩者不能混稱。Tab 順序、焦點、捲動及錯誤提示需可操作。

## 3. 技術與程式可讀性

採 C#、.NET 10 LTS、WPF 原生 Fluent、Microsoft.Data.Sqlite、明確 SQL migration、小型 MVVM。先不混用第三方 UI 主題；只有遇到具體且無法合理解決的元件問題才記錄原因並引入依賴。

四個 projects：

```text
WordTrail/
  src/WordTrail.Desktop/          # Views、ViewModels、程式啟動與組裝
  src/WordTrail.Core/             # 詞義、複習規則、少量服務契約
  src/WordTrail.Infrastructure/   # SQLite、FSRS adapter、Codex process、TTS、備份
  tests/WordTrail.Tests/          # 單元、資料庫整合與固定測試資料
  content/                       # 版本化詞庫，不放個人學習資料
  scripts/                       # build、test、publish、verify
  docs/                          # 操作、架構、決策、驗收證據
  .github/workflows/             # Windows 建置與測試
```

依賴方向：Desktop 引用 Core／Infrastructure，Infrastructure 引用 Core，Core 不引用 UI 或 SQLite。不加微服務、MediatR、CQRS、通用 Repository、事件匯流排、多模型路由或額外狀態機框架。介面只用於確實需要替換或測試的時鐘、排程、AI、語音及資料操作邊界。

重要呼叫鏈可沿檔案閱讀：ReviewViewModel → ReviewService → SchedulerAdapter + SqliteStore。統一的小型 AsyncCommand 負責執行中禁止重按、取消、錯誤處理與恢復，不在每個 ViewModel 重複 async void。中文註解解釋理由與不直覺規則，變數／類別採清楚英文命名。

SDK 與 NuGet 使用已驗證的穩定版並鎖版本；global.json 固定 SDK，packages.lock.json 保留依賴。新增依賴先確認授權，提供 THIRD_PARTY_NOTICES。FSRS.Core 是候選，不能僅因 NuGet 存在就視為驗證通過。Python 僅用於開發期參考測試，不成為最終程式執行需求。

## 4. 資料與狀態契約

| 資料 | 責任 |
| --- | --- |
| WordEntry | 穩定 ID、字面詞條、單字／片語種類 |
| Sense | 穩定 ID、詞性、指定繁中詞義、多義提示、建議優先級、封存資訊 |
| Category／SenseCategory | 詞義與分類多對多；同義多分類不複製卡片 |
| Example／Collocation | 連到 SenseId、雙語文字、各自來源，不把全卡籠統標 Oxford |
| ContentOrigin／ReferenceLink | AI／使用者／授權內容來源，與「到辭典查阅」入口分開 |
| LearningSelection | 候選、選入、暫時略過及篩選進度；熟悉度不製造 FSRS 評分 |
| ReviewCard | SenseId、卡型、FSRS 狀態／step／stability／difficulty／due／lastReview、暫停、版本 |
| ReviewLog | 評分、UTC 時間、操作 ID、前後排程、演算法參數版本、撤銷標記 |
| ContentPackImport | 資料包 ID／版本、穩定項目 ID、使用者修改狀態 |

v1 每詞義只有一張識義卡，資料庫唯一限制 (SenseId, CardType)。未學習以 LastReview 為空識別；Learning／Review／Relearning 由 FSRS 控制；Due 由時間比較，Paused 獨立。正面／背面是 UI 暫存狀態。

所有時間保存 UTC，顯示才轉本機時間，測試使用可替換 TimeProvider。若目前時間早於該卡最後複習時間，拒絕此次提交並解釋時鐘異常，不改資料。

Review 提交須有 OperationId 和 ExpectedScheduleVersion。單一 App 程序、共同防重入入口、資料庫 operation 唯一限制。更新排程、寫紀錄、遞增版本同一交易，commit 後才能換卡。重試同 operation 不重複寫入，舊 version 不覆蓋新排程。

Undo 只限當次複習最後一筆；驗證事件／version，還原完整前置排程、標記原紀錄撤銷、再次遞增版本。離開該次複習後不提供跨工作階段 Undo。統計排除撤銷紀錄，不做完整事件溯源。

明確啟用每條 SQLite 連線的外鍵檢查；SQL 全部參數化，設定合理 busy timeout。耗時資料操作在 UI 執行緒外串行處理，不誤以為 Microsoft.Data.Sqlite 的 Async 方法能提供真正非同步 I/O。Migration 及版本號同交易；遇較新或損壞資料庫不得默默另建空庫。

## 5. FSRS 採用與失敗處理

先以固定版本 FSRS.Core 對照固定版本／commit 的官方 py-fsrs；兩邊使用同一份明示的 21 權重、0.9 目標保留率、1／10 分鐘 learning steps、10 分鐘 relearning、相同最大間隔與 fuzz=false。不能拿各自不同預設權重比較再斷言演算法錯誤。

交付固定時間的參考 fixtures 與生成腳本。對照新卡四級評分、所有學習階段四級評分、同日反覆、跨日、長期逾期、最大間隔、多次完整序列。狀態／step／整數間隔一致；日期差只允許已說明的精度差；浮點採已明訂的絕對／相對容許差並記錄。

如果套件不通過，先排除參數／時間精度差異；仍有問題則在同一 adapter 後修正固定來源的 C# 實作，保留 MIT 授權及變更說明，再跑原測試。不能靜默換成固定間隔或 SM-2。若仍未通過，明確列為未完成阻礙，不宣稱排程可靠。

## 6. 教材與來源品質

300 個詞義或固定片語涵蓋一般職場、會議聯絡、招聘、訂購付款、運送、旅行住宿、設施及日常服務；不以字形變化重複湊數。建議優先級分基礎／優先／延伸，不冒稱官方多益必考清單、CEFR 認證或保證分數。

每筆包含穩定 SenseId、詞條與種類、詞性、精準繁中釋義、分類、1–2 個搭配、至少一個英文例句與對應繁中；重複字面詞條有多義時加情境提示。AI 編寫／agent 審查的教材如實標示，不能稱為人工校訂或牛津提供。

300 筆皆跑欄位、唯一 ID、重複詞義、關聯完整、空例句及來源檢查；另由非作者 agent 逐批審查全部內容的詞義、詞性、搭配、例句對應及翻譯。紀錄修正，不以資料 schema 正確代表英文正確。資料包重匯入使用穩定 IDs，不能重置複習或覆蓋使用者修改。

不複製未獲授權的商業辭典或試題。初版以自編／AI 教材與外部辭典查閱入口完成；開啟連結不等同取得永久儲存授權。將來加授權 API 時逐資源保存 attribution／license，需再檢查實際條款。

## 7. Codex 訂閱整合

ChatGPT 訂閱與一般 OpenAI API 計費分開；採官方本機 Codex CLI 作候選整合，使用正常 ChatGPT 登入，不提取登入 token，也不直接呼叫未公開端點。訂閱額度、模型可用性及登入壽命是外部條件，不能承諾不限量或離線生成。

Phase 0 先驗證官方 CLI 版本、登入狀態、一次小型 JSON 內容產生、stdout／stderr 契約、逾時與取消。以 C# ProcessStartInfo.ArgumentList 與 stdin 傳送輸入，避免 shell 拼字串與使用者文字變命令；直接使用可執行檔，不增加 Node／Python SDK 的正式執行依賴。

使用專用工作目錄、忽略使用者／專案額外設定、短生命週期 session，停用不必要 shell、web、MCP、hooks 與 skills；實際 flags 要經本機 CLI／官方文件與測試確認，不以 prompt 當權限隔離。若無法驗證工具隔離，標示此限制並不把它當成純文字 API 已驗收。最終程式不將整個詞庫、檔案或環境變數交給生成器，只送選定詞義與必要內容。

AI 工作同時最多一件，明確點擊才呼叫；預設每天最多 10 次生成，可改更低。字數、輸出 schema、輸入長度均有限制；不背景補完 300 詞、啟動自動生成或遇錯誤無限重試。不自動購買額度，不在訂閱失敗時退回 API 付費。

结果處理：退出碼、最終完整 JSON、指定 SenseId、必要欄位、數量／長度都驗證；拒答、額度用完、登入失效、找不到 CLI、逾時、格式錯誤明確顯示。取消後終止該次子程序樹並忽略晚到回應；不終止使用者其他 Codex 程序。內容預覽確認後保存，附來源、模型（可取得時）、時間、提示範本版本。

若訂閱整合試驗未通過，完成離線功能及固定格式手動匯入，將 AI 自動生成列為未完成外部依賴；不能把 mock 當成真實串接。一般 API 可作後續獨立 adapter，但本次不實作兩套 provider 平台，也不索取 API key 或產生 API 帳單。

發音採 Windows TTS；Codex 訂閱文字生成不等於 speech API。確認實際可用英文 voice；沒有時顯示原因與辭典入口，不以中文 voice 念英文來假裝功能正常。

## 8. 備份、隱私與復原

正式資料放 %LOCALAPPDATA%\WordTrail，執行檔目錄只放程式。測試固定使用獨立暫存資料目錄，不讀寫正式詞庫。無 telemetry；診斷只含必要事件／錯誤代碼，避免記錄憑證或整段私有筆記。

備份用 BackupDatabase 取得一致 SQLite 快照，再加 manifest（格式版本、DB schema、檔案清單／SHA-256）；如有合法可存媒體才加入。先寫暫存 ZIP，驗證後改正式檔名。讀寫與維護操作共用簡單鎖；不直接複製開啟中的 .db 或遺漏 WAL。

還原固定整庫替換，不做合併；先到 staging 驗證 ZIP 路徑／大小／雜湊／版本、DB integrity_check／foreign_key_check。先成功備份舊庫，再關閉連線／清理 pool，再替換；遇任何失敗可回到舊庫。未知新版／壞庫／缺檔拒絕，不破壞現在資料。不把檔案系統替換誤當 SQL transaction 能自動保護。

提供手動備份、升級／還原前備份及開啟備份目錄；不做一直執行的背景服務。同磁碟備份不能抵抗磁碟故障，README 說明如何自行另存位置。

Git／ZIP／備份排除個人 DB、複習歷史、Codex 認證、API keys、.env、暫存輸出與使用者日誌。教材資料包可以提交，個人使用資料不能。SHA-256 用於檢查損壞，不宣稱檔案來源真偽驗證。

## 9. Git、交付與自主執行

建議路徑 C:\Users\ASUS\Desktop\Codex\WordTrail，獨立 Git repository；不操作上層既有 repository，也不修改 global safe.directory。初期先提交規格與審查，實作使用 codex/initial-implementation 分支，依可檢查的里程碑提交。最終乾淨工作樹、標記 v0.1.0。

GitHub 採 ming0071/WordTrail，private，已依後續「建立專案並推上 GitHub」指示建立；不覆寫既有 repository，不 force-push。不把其他專案帶入新 repo。推送前檢查 staged 檔案與常見秘密模式；核對遠端與 commit SHA。

GitHub Actions 採 Windows、最小 contents:read、無 AI 憑證的還原／建置／測試／打包；固定測試輸出與 ZIP artifact。private repo 的 Actions 有帳號配額與設定限制；無額度／被停用時保留本機完整證據，註明雲端 gate 未通過，不買額度或擅改付款設定。

release ZIP 採 win-x64 self-contained、先不 trimming／NativeAOT／單檔壓縮；附 checksum、版本與第三方授權。從新空資料夾解壓後啟動 EXE 做 smoke test，而非只驗證 dotnet run。資料保留在 LocalAppData，換新程式包不重置資料。

文件包括：快速上手、從 clone 到 build/test/publish、資料位置與備份還原、架構導覽、詞庫編修規則、AI 限額／登入錯誤處理、已知限制、驗收紀錄。說明改一個欄位、改 UI、改排程參數、改 AI 提示各去哪些檔案。

自主執行次序：

1. Phase 0：環境、GitHub／Codex認證、官方 CLI 最小生成、UI 自動化、英文語音與 SDK 可用性。記錄真正需要本人登入／授權的缺口。
2. Phase 1：固定資料契約、FSRS 對照、SQLite migration、review transaction／Undo、備份還原及故障測試。
3. Phase 2：五個畫面與完整離線流程；同時分派教材撰寫與獨立審查，介面契約固定避免多 agent 改同檔。
4. Phase 3：Codex 正式 adapter、預覽保存、TTS、來源與錯誤提示；有意義的整合測試。
5. Phase 4：agent 依實際 diff 再做架構／資料安全／學習內容對抗審查，修正 P0／P1 並重跑相關測試。
6. Phase 5：乾淨 release 包實機驗收、文件、GitHub 推送、CI、交付路徑與已知限制。

一般設計選擇、bug 修復、測試、文檔、局部重構可自主處理。不在不確定情況下自動擴大到付費 API、public 發布、額外雲端服務或不相關專案。遇外部阻礙先完成其餘工作，保留可重現紀錄，不謊稱完整交付。

## 10. 完成標準與測試

| Gate | 必須有的證據 |
| --- | --- |
| G1 可建置 | 乾淨 restore/build 成功；SDK／NuGet 版本固定 |
| G2 FSRS | 共同參數參考 fixtures 通過，包含四級評分與多階段序列 |
| G3 寫入可靠 | 故障注入、重複 OperationId、舊 version、Undo、時間倒退、重啟測試 |
| G4 詞庫正確 | 300 筆結構檢查、全量獨立內容審查、多義提示、重匯不重置進度 |
| G5 真實 UI | 新增→分類→搜尋→翻卡→評分→重啟完整操作；空／長／錯誤狀態截圖檢查 |
| G6 AI | 固定 fake process 覆蓋輸出／退出／逾時／取消／晚到；至少一次訂閱真實生成與保存 |
| G7 發音 | 英文 voice 偵測、非空合成輸出、重播停止；音訊是否人工聽辨另行標示 |
| G8 復原 | 備份還原 IDs／內容／歷史／排程相等；壞ZIP／新版schema／缺檔／失敗仍保留舊庫 |
| G9 發布包 | 從乾淨目錄解壓 EXE 實際操作，SQLite native libraries 隨包，換包資料保留 |
| G10 GitHub | private repo URL、推送 commit SHA、CI 結果、可取得 ZIP／checksum；缺項明列 |

最低故障測試：評分更新後 log 前中斷；相同 operation 重送；按鍵與滑鼠連按；Undo 碰上新 version；暫停／封存卡仍在舊佇列；migration 失敗回滾；較新 schema 拒絕；備份途中取消；還原替換失败；路徑穿越 ZIP；AI 呼叫返回錯 SenseId、截斷 JSON、缺例句、非零退出、失聯、取消後晚到；無網路核心仍可使用。

不以測試數量或 coverage 百分比取代使用流程驗收。不為純樣式或直接映射 property 寫機械式測試。測試不消耗真實 AI 額度，真實連線僅少量 smoke tests 並另記錄。

## 11. 後續短文擴充的預留契約

現在保留穩定 SenseId、來源、review log、migration；不先造未使用空表。若使用者選擇首版也做短文，加入以下完整 scope，否則它們都留下一版：

- 每篇 100–150 英文字、3 題四選一，整個活動目標约 5 分鐘；主旨、明示細節、簡單改寫理解。
- 選 3–5 個同情境詞義，優先今日已複習者（不能先複習後只查當下到期而選不到詞），不足補近期已學者；無合適詞則顯示先學單字，不強迫湊文章。
- Exercise 保存文章、目標 SenseIds、穩定題目／選項 IDs、固定答案、解析／證據句、來源及生成版本；Attempt 分開保存選擇、時間、提示／翻譯使用狀態。
- 重生建立新版本，不改已作答的題目；正確答案按固定 ID 評分，不讓 LLM 每次重新判分。
- 有 schema／語意規則檢查、可標記壞題並排除成績；結構化輸出不是語言正確保證。
- 看文章／選對題不更新任何 FSRS。至少一篇明示為離線示範的固定教材供測試。
- 不將正確率換算 TOEIC 分數。聽力活動、圖譜仍不因此自動加入首版。

## 12. 環境與授權核對紀錄

2026-09-26 已確認：Windows build 26100 x64；Git 2.41.0；GitHub 正式 /user 讀取成功為 ming0071，現有 OAuth scopes 包含 repo、workflow；本機 Codex 在正常使用者環境顯示 Logged in using ChatGPT。沙箱內無法取用認證庫不代表使用者未登入。檢查未顯示或保存 token。

已確認 Microsoft Zira Desktop（en-US）可被 System.Speech 列出；Computer Use 初始化與原生視窗列舉成功。這些只證明前置能力，尚未證明成品 UI／音訊驗收通過。

PATH 與標準安裝路徑尚未找到 dotnet SDK、gh CLI。使用者後續明確選擇這台不需 Visual Studio，先建立專案推 GitHub，另台拉下來編譯；因此不在這台安裝 Visual Studio 或 SDK，以 Windows GitHub Actions 執行建置／測試／打包，再下載自足包驗收。GitHub 使用現有 Git credential 配合官方 REST，gh 不是必要依賴。SDK允許 .NET 10 穩定服務更新，實際版本記錄於 CI。實作期工具下載／native測試仍依當時沙箱核准流程。

後續執行採已告知的核心第一版與私人 repository；短文、聽力理解練習及關聯圖留待後續。Codex 最小試驗與正式 C# 生成均已成功，具體版本與驗證邊界見 VALIDATION.md。需要 GUI 驗收時保持 Windows 桌面可用；電腦關機／睡眠／鎖定或帳號要求重新登入可能阻礙對應部分。不得自動調低系統安全設定；使用者停止 Computer Use 後停止視窗操作。

## 參考依據

- [.NET 10 LTS](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [WPF .NET 10 Fluent](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [SQLite async 限制](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)
- [SQLite online backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)
- [FSRS.Core](https://github.com/TranPhucTien/FSRS.Core)／[py-fsrs](https://github.com/open-spaced-repetition/py-fsrs)
- [Codex 登入](https://learn.chatgpt.com/docs/auth)／[程式整合](https://learn.chatgpt.com/docs/codex-sdk)／[非互動模式](https://learn.chatgpt.com/docs/non-interactive-mode)
- [ChatGPT／Codex 訂閱](https://learn.chatgpt.com/docs/pricing)／[API 計費](https://developers.openai.com/api/docs/pricing)
- [Oxford FAQ](https://developer.oxforddictionaries.com/faq)／[Merriam-Webster 條款](https://www.dictionaryapi.com/info/terms-of-service)
