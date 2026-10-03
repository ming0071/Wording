# FSRS Verification

Wording 使用 `FSRS.Core 1.0.7`，並以官方 `py-fsrs 6.3.1` 產生的獨立參考資料驗證排程。Python 僅用於開發，執行 Windows 程式不需要 Python。

[回到主頁](../README.md#documentation) · [評分與複習排程](review-scheduling.md)

## Parameters

兩邊明確使用相同的參數，定義見 [FsrsScheduler.cs](../src/Wording.Infrastructure/FsrsScheduler.cs) 與 [參考資料產生腳本](../scripts/generate_fsrs_fixtures.py)。

| 設定 | 值 |
| --- | --- |
| 參數版本 | `fsrs6-wording-v1-py6.3.1-whole-days` |
| 模型權重 | 固定的 21 個參數 |
| 目標保留率 | 0.9 |
| 新詞學習步驟 | 1 分鐘、10 分鐘 |
| 重新學習步驟 | 10 分鐘 |
| 最大間隔 | 36,500 天；上限測試另設為 30 天 |
| 間隔隨機擾動（fuzzing） | 關閉 |

## Compatibility

`FSRS.Core 1.0.7` 的記憶保持率（retrievability）使用 `TimeSpan.TotalDays`；`py-fsrs 6.3.1` 使用完整經過天數。因此隔 1 天 23 小時複習時，兩者原本會得到不同結果。

`FsrsScheduler` 透過套件的 `IRetrievabilityCalculator` 擴充點改用完整天數，其餘排程使用原套件。參考資料包含非整數天案例，防止套件升級改變這項行為。

## Validation

[fsrs-reference.json](../tests/Wording.Tests/Fixtures/fsrs-reference.json) 目前有 58 個案例，涵蓋初次評分、學習與重新學習、長短間隔、最大間隔及連續評分。[FsrsSchedulerTests](../tests/Wording.Tests/FsrsSchedulerTests.cs) 逐筆比對：

- 卡片狀態與步驟完全相同。
- 到期時間誤差最多 1 ms，上次複習時間完全相同。
- 記憶穩定度與難度的容許誤差為 `max(1e-10, abs(expected) × 1e-10)`。

## Reproduction

在專案根目錄，以已安裝 Python 的開發環境重建參考資料並執行排程測試：

```powershell
python -m pip install fsrs==6.3.1
python scripts/generate_fsrs_fixtures.py
.\scripts\test.ps1 -Filter 'FullyQualifiedName~FsrsSchedulerTests'
```

參考資料已提交；只執行最後一行的 C# 測試時不需要 Python。測試報告位於 `artifacts/test-results/tests-filtered.trx`。

來源：[FSRS.Core](https://www.nuget.org/packages/FSRS.Core/1.0.7)、[官方 py-fsrs](https://github.com/open-spaced-repetition/py-fsrs)。
