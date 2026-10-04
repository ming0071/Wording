# Releases

每個版本的發布說明放在 `<版本>.md`，最新為 [0.3.7](0.3.7.md)。[0.3.0](0.3.0.md) 等已發布版本的說明作為歷史紀錄保留。

[回到主頁](../../README.md#documentation) · [品質檢查與維護](../quality-checks.md)

## Publish

1. 在 `Directory.Build.props` 更新版本，新增對應的發布說明。
2. 正常還原一次，提交更新的套件鎖定檔；以 `scripts/build.ps1 -LockedRestore` 驗證。
3. 將變更推送到 `main`，確認 CI 通過，再建立並推送相同版本的 tag，例如版本 `0.4.0` 對應 `v0.4.0`。

推送 `v` 開頭的版本 tag 後，CI 會重新建置、測試、打包，再交給獨立的 Release 工作。只有這個工作擁有 `contents: write`；一般建置及 PR 仍為唯讀。

附件固定為 Windows ZIP、SHA-256 校驗碼與 `toeic-vocabulary.json`，來自同一次驗證的 artifact。發布器先建立草稿、上傳並核對每個附件的伺服器 SHA-256 與大小，全部成功才公開。失敗時保留草稿，可從 Actions 重新執行；既有正確附件會略過，錯誤附件只會在草稿階段重傳。

已發布版本的附件不會被覆寫。內容完全相同的重試只讀取既有 Release；內容不同則中止。修正公開版本時新增版本與 tag，保留 `v0.3.0` 等既有 tag。預發行版本使用例如 `0.4.0-beta.1`，不設為 Latest。

版本 tag 必須指向本次檢出的 commit；版本號、tag、發布說明和打包內三個專案版本都會驗證。`workflow_dispatch` 只做驗證，不發布。

API 行為依 [GitHub Release 文件](https://docs.github.com/en/rest/releases/releases)與[附件文件](https://docs.github.com/en/rest/releases/assets)實作；跨工作傳遞使用固定 SHA 的官方 [download-artifact](https://github.com/actions/download-artifact/releases/tag/v8.0.1)。
