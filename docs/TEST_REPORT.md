# 測試報告

## 專案資訊

| 項目 | 內容 |
|---|---|
| 專案 | CloudFileManager |
| 測試日期 | 2026-10-07 |
| 執行環境 | Windows、.NET 8 SDK 8.0.425 |
| 測試類型 | 建置檢查、API smoke test、瀏覽器手動互動測試 |
| 測試資料 | 依考題範例畫面建立的 in-memory 目錄樹 |

## 摘要

目前核心工作台與主要操作可執行，建置成功；但 XML 匯出的名稱規則有缺陷，因此 XML 驗收尚未通過。專案目前沒有自動化測試專案，以下結果是本次執行的建置與瀏覽器手動驗證，不代表完整回歸測試。

**整體狀態：部分通過，尚不建議以目前版本作為最終繳交版。**

## 測試結果

| ID | 測試項目 | 結果 | 驗證結果 |
|---|---|---|---|
| BLD-01 | `dotnet build` | 通過 | Build 成功，0 警告、0 錯誤。 |
| WEB-01 | 開啟工作台首頁 | 通過 | ASP.NET Core 頁面正常載入，顯示檔案樹、Visitor／Observer 面板與操作 Console。 |
| DATA-01 | 考題範例目錄樹 | 通過 | 顯示 9 個節點：4 個目錄（含根目錄）與 5 個檔案；名稱、明細與層級符合題目範例。 |
| VIS-01 | 容量計算 Visitor | 通過 | 遍歷 9 個節點，總容量顯示為 2.69 MB。 |
| VIS-04 | 任意目錄容量計算 | 通過 | 個人筆記為 201 KB／4 節點；Archive_2025 為 200 KB／2 節點；選取檔案時改算其父目錄。 |
| VIS-02 | 副檔名搜尋 Visitor | 通過 | 搜尋 `.docx` 找到 2 筆，顯示完整路徑與遍歷紀錄。 |
| VIS-03 | 部分檔名搜尋 | 通過 | 搜尋 `舊會議記錄` 找到 1 筆完整路徑。 |
| OBS-01 | Observer 進度與節點 | 通過 | 操作時更新目前節點、進度百分比與節點計數。 |
| CMD-01 | 標籤、Undo／Redo | 通過 | 瀏覽器操作確認標籤可切換，Undo 與 Redo 可還原／重做標籤變更。 |
| CMD-02 | 新增檔案與 Undo | 通過 | 伺服器檢查後確認新增節點；新增命令可復原。 |
| CMD-03 | 複製、貼上與重名處理 | 通過 | 複製兩次貼到同一目錄，自動命名 `(copy)` 與 `(copy 2)`；Undo／Redo 可復原貼上及刪除。 |
| FILE-01 | DOCX 頁數檢查 | 通過（Office 頁數屬性） | 伺服器檢查考題 DOCX 的 `docProps/app.xml` 並讀出 7 頁；結果唯讀，確認新增只送短效檢查 token。 |
| FILE-02 | 圖片尺寸檢查 | 通過 | 上傳有效測試 PNG，伺服器以 ImageSharp 解碼並讀出 `4×5`；解析結果唯讀。 |
| FILE-03 | 純文字編碼檢查 | 通過 | ASCII 測試檔由伺服器判為 ASCII；結果唯讀，建立節點後保留該值。 |
| FILE-04 | 無效／不支援檔案 | 通過 | 損毀 DOCX 回傳檢查錯誤且不提供預設頁數；舊式 `.doc` 明確不支援。 |
| XML-01 | XML 格式比對與解析 | 通過 | `/api/export/xml` 回傳 HTTP 200；瀏覽器 `DOMParser` 驗證合法。根節點、資料夾／檔案名稱、順序、頁數、解析度、編碼與 500B README 均符合題目範例。 |
| PATH-01 | 中文顯示名與英文儲存路徑 | 通過 | `專案文件` 顯示中文、API path 為 `Root/Project_Docs`；複製到 `Root/Personal_Notes` 成功，XML 使用題目指定 alias。 |
| UI-01 | 手機版寬度 | 通過 | 390px viewport 下文件寬度未超出 viewport；工作台改為單欄排列。 |
| SORT-01 | 名稱、大小、副檔名升降冪排序 | 部分完成 | 每個欄位第一次按降冪、再次按升冪；箭頭及輔助標籤更新。排序行為通過；比較策略尚未抽成 Strategy Pattern。 |
| UI-02 | 複製、貼上、標籤與刪除圖示 | 通過 | 使用文件、剪貼簿、吊牌及垃圾桶圖示；按鈕仍保留文字、提示與無障礙名稱。 |
| TEST-01 | 自動化測試套件 | 未建立 | 專案內沒有測試專案或自動化測試案例。 |

## 重現方式

在專案根目錄執行：

```powershell
dotnet build
dotnet run --urls http://localhost:5083
```

瀏覽器開啟 `http://localhost:5083`。檔案檢查 API 為 `POST /api/files/inspect`，確認新增 API 為 `POST /api/commands/create-file`；XML 匯出為 `/api/export/xml`；Visitor API 為 `POST /api/visitor/size` 與 `POST /api/visitor/search?query=.docx`。

新增檔案分為檢查與確認兩步。檢查 API 讀取本機上傳內容並在伺服器解析；確認新增只接受短效檢查 token。結果欄位皆唯讀。節點分別保存中文 display name、英文 storage name 與 XML name；目前英文路徑是記憶體模型中的邏輯路徑，尚未在磁碟建立實體目錄。檔案不會永久保存；ASP.NET Core 可能在單g次請求期間將 multipart 內容暫存於記憶體或暫存目錄。單檔上限為 20 MB。

## 已知限制與後續驗收

1. DOCX 頁數取自 `docProps/app.xml`，可能缺少或過期；此時會拒絕新增。舊式 `.doc` 不支援頁數擷取。
3. 文字編碼可辨識 BOM、ASCII 與 UTF-8；其他無法可靠辨識的編碼會拒絕新增。
4. 若交付設計要求 Strategy Pattern，將既有排序比較策略抽成介面與實作。
5. 為容量、搜尋、XML、命令復原與檔案檢查補上自動化測試。
6. 英文 storage path 目前是記憶體模型中的穩定識別，尚未映射到實體磁碟；重啟 Web 應用程式會還原範例資料。
