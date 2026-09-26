# 驗收紀錄

更新：2026-09-26。開發進行中，以下只記錄已實際觀察的證據。

- 私人 repository 已建立：https://github.com/ming0071/WordTrail 。
- GitHub /user 認證成功，現有 repo／workflow scopes 可用；未輸出憑證。
- 在正常使用者環境，Codex CLI 顯示 ChatGPT 登入。
- 一次最小訂閱生成成功，退出碼 0；JSONL 為 thread.started、turn.started、item.completed:agent_message、turn.completed，沒有工具事件。未使用 API key。
- 首次參數試驗發現本機 CLI 不接受 `tools.view_image`；已移除不相容選項。不能以官方某版本設定表代替實際 CLI 相容性測試。
- Windows 語音列舉找到 Microsoft Zira Desktop（en-US）；尚未對成品驗收發音。
- Computer Use 原生視窗列舉成功；尚未對成品 UI 驗收。
- 此電腦不安裝 Visual Studio／.NET SDK；建置、測試與打包交給 Windows GitHub Actions。

待完成：CI build／tests、全部教材 QA、FSRS oracle、備份故障測試、成品 UI／ZIP 啟動、程式內 Codex 生成、實作後獨立審查。未通過的 gate 不會改寫成成功。
