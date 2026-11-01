# Releases

最新版本為 [5.0](5.0.0.md)，版本號 5.0.0。改以單一 Windows EXE 安裝包發布，內含完整教材，支援程式內版本通知與下載更新。舊 Release 0.2.0、0.3.0、0.4.8 保留；0.4.0 與 0.3.7 的 Release 已移除，更新說明與 Git tag 保留。

[回到主頁](../../README.md#documentation) · [品質檢查與維護](../quality-checks.md)

## Publish

本機執行 `scripts/publish.ps1 -LockedRestore` 會產生 `artifacts/Wording-版本-win-x64-Setup.exe`，並同步開發用固定入口 `artifacts/Wording-current/Wording.exe`。需要保留執行中的固定入口時，可加 `-SkipCurrentUpdate` 先建置安裝包，稍後使用 `scripts/update-current.ps1` 替換。

安裝編譯器由 `scripts/install-inno.ps1` 取得固定版本的官方 Inno Setup，下載後先核對 SHA-256，放在忽略提交的 `.tools`。程式發布資料夾仍保留作開發與驗證使用，對外不再提供 ZIP 程式包。

1. 在 `Directory.Build.props` 更新版本，新增對應的發布說明。
2. 正常還原一次，提交更新的套件鎖定檔；以 `scripts/build.ps1 -LockedRestore` 驗證。
3. 將變更推送到 `main`，確認 CI 通過，再建立並推送相同版本的 tag，例如版本 `0.4.0` 對應 `v0.4.0`。

推送 `v` 開頭的版本 tag 後，CI 會重新建置、測試、打包，再交給獨立的 Release 工作。只有這個工作擁有 `contents: write`；一般建置及 PR 仍為唯讀。

唯一公開附件為 Windows x64 安裝 EXE。校驗碼與 `installer-validation.json` 只作為 CI 與發布器的內部輸入，不要求使用者下載。教材完整放進安裝包。

CI 先驗證發布目錄的依賴、版本、教材、授權及每個檔案的 SHA-256 清單，再執行 `scripts/test-installer.ps1`，實際安裝測試舊版、升級、執行安裝後程式讀取自訂詞庫、解除安裝並核對學習資料保留。只有報告與該 EXE 的雜湊、版本完全一致，發布器才允許上傳。

發布器先建立草稿、上傳並核對附件的伺服器 SHA-256 與大小，成功才公開。失敗時保留草稿，可從 Actions 重新執行；既有正確附件會略過，錯誤附件只會在草稿階段重傳。客戶端依 GitHub 的附件摘要校驗新版 EXE，不需要 GitHub 帳號或手動下載校驗碼。

已發布版本的附件不會被覆寫。內容完全相同的重試只讀取既有 Release；內容不同則中止。修正公開版本時新增版本與 tag，保留 `v0.3.0` 等既有 tag。預發行版本使用例如 `0.4.0-beta.1`，不設為 Latest。

版本 tag 必須指向本次檢出的 commit；版本號、tag、發布說明和打包內三個專案版本都會驗證。`workflow_dispatch` 只做驗證，不發布。

API 行為依 [GitHub Release 文件](https://docs.github.com/en/rest/releases/releases)與[附件文件](https://docs.github.com/en/rest/releases/assets)實作；跨工作傳遞使用固定 SHA 的官方 [download-artifact](https://github.com/actions/download-artifact/releases/tag/v8.0.1)。
