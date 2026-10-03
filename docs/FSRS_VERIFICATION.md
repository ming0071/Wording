# FSRS 參考驗證

- 正式依賴：`FSRS.Core 1.0.7`，參數版本 `fsrs6-wording-v1-py6.3.1-whole-days`。
- 獨立參考：官方 `py-fsrs 6.3.1`。Python 僅用於開發，不隨 Windows 程式執行。
- 兩邊明確使用相同的 21 個權重、0.9 保留率、1／10 分鐘 learning steps、10 分鐘 relearning、36500 天最大間隔、關閉 fuzz。特別案例把最大間隔設為 30 天。
- `scripts/generate_fsrs_fixtures.py` 產生 `tests/Wording.Tests/Fixtures/fsrs-reference.json`，目前 58 個案例。
- 狀態和 step 完全相同；到期時間誤差最多 1 ms；浮點絕對及相對容許誤差各 `1e-10`。

## 已確認並修正的差異

FSRS.Core 的 retrievability 使用 `TimeSpan.TotalDays`；py-fsrs 6.3.1 使用完整經過天數。因此隔 1 天 23 小時複習時，兩者會得到不同的記憶保持率。

`FsrsScheduler` 透過套件提供的 `IRetrievabilityCalculator` 擴充點，改用完整天數。其餘排程繼續使用原套件；沒有複製整套演算法，也沒有退回固定間隔。fixtures 明確包含非整數天案例，防止未來升級悄悄移除修正。

## 重現

```powershell
python -m pip install fsrs==6.3.1
python scripts/generate_fsrs_fixtures.py
dotnet test --filter FullyQualifiedName~FsrsSchedulerTests
```

沒有 Python 時仍可直接執行 C# 測試，因為參考資料已提交。

來源：[FSRS.Core](https://www.nuget.org/packages/FSRS.Core/1.0.7)、[官方 py-fsrs](https://github.com/open-spaced-repetition/py-fsrs)。
