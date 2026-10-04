# Feature Showcase

主 README 並排展示複習與閱讀，更多畫面收在可展開區。PNG 保留文字與漸層，不自動播放；點圖片可查看原尺寸，主頁不需要把全部畫面攤開。

[回到主頁](../../README.md#usage) · [桌面介面導覽](../../src/Wording.Desktop/README.md) · [情境練習](../scenario-practice.md)

## Screens

| 畫面 | 展示內容 |
| --- | --- |
| [複習](wording-review.png) | 同詞多義、單一星號、雙語例句與 1–4 評分 |
| [閱讀](wording-reading.png) | 情境文章、英文選擇題與提交入口 |
| [單字庫](wording-library.png) | 主題、排序、星號與多組解釋 |
| [練習設定](wording-practice-setup.png) | 閱讀／聽力、主題、類型、長度與難度 |
| [聽力](wording-listening.png) | 播放、暫停、速度與提交前隱藏的逐字稿 |
| [解析](wording-results.png) | 分數、答案、中文解析與原文依據 |
| [單字回饋](wording-word-feedback.png) | 個別 FSRS 評分、每日新詞資格與標星 |

## Regenerate

素材由 [Wording.Showcase](../../scripts/showcase/Program.cs) 載入真正的 MainWindow、ViewModel 與 WPF 控制項，以 192 DPI 輸出無損 PNG。這個文件工具不加入產品或發行包；每次啟動建立獨立的暫存 SQLite 詞庫，使用教材的部分詞條、固定原創文章和題目，以及示範練習次數。沒有讀取正式資料、呼叫 Codex 或消耗 AI 額度；播放狀態使用無聲替身，不驗證實際語音裝置。

Windows 上，在專案根目錄執行：

```powershell
dotnet build scripts/showcase/Wording.Showcase.csproj -c Release
dotnet scripts/showcase/bin/Release/net10.0-windows/Wording.Showcase.dll --capture docs/images
```

重新產生後，檢查每張圖的文字、捲動位置與按鈕，再確認主 README 的並排版面。展示的是當前開發版本；實際文章由使用者設定的 Codex 模型生成，內容與示範不同。

## GitHub Layout

GitHub README 支援[圖片與相對連結](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax)，以及包含圖片的 [details 折疊區](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/organizing-information-with-collapsed-sections)。因此採用兩欄預覽、點圖放大、展開更多畫面。

IG 式的左右滑動輪播需要自訂互動程式；GitHub 的[渲染流程](https://github.com/github/markup#github-markup)會移除 script 與自訂樣式，不能直接放進 README。若日後需要真正輪播，可另做展示網頁，再由主 README 連過去。
