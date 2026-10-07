# 變更紀錄

本檔記錄本次工作階段中可確認的專案變更；由於目前環境沒有可用的 Git 命令，以下不是 commit 清單，也不代表完整版本庫歷史。

## [Unreleased] - 2026-10-07

### Added

- 將專案改為 ASP.NET Core Web App，新增靜態工作台與 JSON／XML API。
- 新增依考題範例建立的目錄樹，包含 Word、圖片、文字檔與巢狀資料夾。
- 新增 Visitor 容量計算、副檔名／檔名搜尋、XML 匯出，以及記錄遍歷路徑的 Observer。
- 新增 Command 管理器與標籤、複製、貼上、刪除、Undo、Redo 操作；貼上重名會自動加上 copy 序號。
- 新增從本機選取 Word、圖片、純文字檔，伺服器檢查後再確認建立節點的流程；新增檔案使用 Command，支援 Undo／Redo。
- 新增依名稱、大小、副檔名排序的工作台控制項。
- 新增桌機三欄及手機單欄的響應式介面，呈現檔案樹、Visitor／Observer 操作與 Console 紀錄。
- 新增本次驗證結果與未完成項目，詳見 [`docs/TEST_REPORT.md`](docs/TEST_REPORT.md)。

### Changed

- 工作台樣式依使用者提供的考題範例畫面調整。
- 範例資料改為 XML 題目指定的根目錄、資料夾階層、檔案、頁數與 500B README；UI 顯示 Urgent／Work／Personal 標籤。
- 節點名稱分為中文 UI display name、英文 storage path component 與 XML alias；API 操作用英文路徑，前端仍顯示中文。
- 搜尋 Visitor 支援副檔名與部分檔名查詢。
- 容量 Visitor 依目前選取目錄計算子樹；選到檔案時改以其父目錄為計算範圍。
- 新增檔案以短效檢查 token 串接檢查與確認；頁數、解析度、編碼由伺服器讀取並以唯讀欄位呈現，確認時不接受前端自填 metadata。
- DOCX 頁數取自 Office `docProps/app.xml`；圖片尺寸以 ImageSharp 解碼；文字編碼依 BOM／嚴格 UTF-8 檢查。
- 舊式 `.doc`、缺少已儲存頁數屬性的 DOCX，以及無法可靠判定編碼的文字檔會明確拒絕，不填入預設值。
- 複製／貼上控制恢復至工具列，提供文件、剪貼簿圖示；刪除使用垃圾桶圖示，標籤使用吊牌圖示。
- 排序按鈕第一次點擊為降冪，第二次為升冪，並以箭頭顯示目前方向。
- 上傳內容不永久保存，但 ASP.NET Core 可能在單次請求期間使用記憶體或暫存目錄緩衝；單檔限制 20 MB。

### Fixed

- 修正原始程式的字串插值、TagCommand 參數識別字、Parent nullability、命名空間結尾與應用程式進入點等建置問題。
- 修正 XML Visitor 巢狀目錄輸出時的結尾標籤流程。
- 改用 `XmlWriter` 輸出並編碼非法 XML 名稱；瀏覽器 XML parser 驗證通過，結構與題目指定範例一致。

### Known Issues

- 排序升降冪行為已實作；比較策略尚未抽成 Strategy Pattern。
- DOCX 頁數屬性可能缺少或過期；此時會拒絕新增。舊式 `.doc` 尚不支援。
- 文字檔目前可靠辨識 BOM、ASCII 與 UTF-8；其他編碼會拒絕新增。
- 英文 storage path 目前是記憶體模型中的邏輯識別，尚未映射到實體磁碟；服務重新啟動後目錄樹會重置。
- 尚無自動化測試專案；已執行的建置與瀏覽器手動測試見測試報告。
