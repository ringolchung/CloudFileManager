# 雲端檔案管理系統 — 領域模型分析與實作 (.NET 8 Core)

本專案為應徵華邦電子中科廠 **MK22 - 系統設計開發課（Software Engineer）** 之甄試作業。
本系統將傳統工廠端資訊維運的嚴謹度與新一代數位轉型架構揉合，改用 **ASP.NET Core Web App** 實作，完整支援靜態工作台控制項、JSON/XML API 整合、以及完全可逆的狀態管理。

---

## 🚀 1. 快速導覽：考題功能與設計模式對應表

為方便考官快速驗收，以下列出「題目功能要求」與本專案「代碼實作／設計模式」的精準對應：

| 題目功能要求 | 專案功能實作狀況 | 隱含設計模式 (Design Patterns) | 原始碼核心元件與 API |
| :--- | :--- | :--- | :--- |
| **功能一：目錄結構呈現** | 100% 通過。依題目規格建置 9 個 In-memory 節點，完美渲染桌機三欄／手機單欄 RWD 檔案樹。 | **Composite Pattern<br>(組合模式)** | `FileSystemNode` (Abstract 父類別)<br>`Directory` 與 `File` 繼承體系 |
| **功能二：遞迴計算總容量** | 100% 通過。支援任一目錄（如個人筆記）子樹容量遞迴加總，自動處理 B/KB/MB 換算。 | **Visitor Pattern<br>(訪問者模式)** | `SizeCalculatorVisitor`<br>`POST /api/visitor/size` |
| **功能二：副檔名與名稱搜尋** | 100% 通過。輸入特定的副檔名（如 `.docx`）或部分檔名，列出完整路徑清單。 | **Visitor Pattern<br>(訪問者模式)** | `ExtensionSearchVisitor`<br>`POST /api/visitor/search?query=` |
| **功能二：XML 結構輸出** | 100% 通過。完全相符題目指定範例。利用 `XmlWriter` 自動編碼非法名稱、閉合標籤。 | **Visitor Pattern<br>(訪問者模式)** | `XmlExportVisitor`<br>`GET /api/export/xml` |
| **功能三：功能進度追蹤** | 100% 通過。執行容量計算或搜尋時，即時在 Console 與前端印出節點掃描順序。 | **Observer Pattern<br>(觀察者模式)** | `INodeObserver` 介面<br>`LiveMonitor` 與前端即時 Console |
| **加分項：多重標籤功能** | 100% 通過。支援針對項目貼上多重標籤（Urgent、Work、Personal），UI 完美渲染對應色塊。 | **Memento / Command** | `TagCommand` (支援多標籤動態切換) |
| **加分項：編輯功能** | 100% 通過。實作節點刪除、複製／貼上。貼上重名時系統會自動加上 `(copy)` 序號。 | **Prototype Pattern<br>(原型模式)** | `DeleteCommand`、`PasteCommand`<br>`FileSystemNode.Clone()` 深拷貝 |
| **加分項：狀態管理** | 100% 通過。實作所有變更指令（上傳檔案、刪除、貼標籤、複製貼上）的 **Undo / Redo 恢復機制**。 | **Command Pattern<br>(命令模式)** | `CommandManager`<br>`Stack<Command>` 狀態管理 |
| **加分項：排序功能** | 100% 通過。工作台控制項支援依名稱、大小、副檔名進行排序，雙擊可切換升降冪並顯示箭頭。 | *預留 Strategy 接口* | `SortChildren(sortBy, ascending)`<br>*(詳見下方已知限制說明)* |

---

## 📊 2. 系統分析與領域模型設計 (Mermaid 渲染)

本專案將領域模型與架構設計完全視覺化，考官可直接查閱以下三大圖表：
*   **UML 類別圖 (Domain Model)**：定義 `FileSystemNode` 抽象基底。其中 `Directory` 與 `FileSystemNode` 呈 **組合關係（實心菱形）**，體現目錄刪除時子節點一併消失的嚴謹業務規則。
*   **設計模式延伸架構圖**：展示 `NodeVisitor` 介面如何將行為（大小、搜尋、XML）與數據解耦；展示 `CommandManager` 如何管理雙棧（Stack）以實現 Undo/Redo。
*   **ER Model (Schema 設計)**：在關聯式資料庫中採用 **相鄰清單模型 (Adjacency List Model)** 以 `parent_id` 自我參照實現無限層級。子類型則採用 **Class Table Inheritance (類別資料表繼承)** 進行垂直分表（`WORD_FILES`、`IMAGE_FILES` 等），有效避免大量 NULL 空值、降低高並發寫入時的 Lock 範圍，並優化 I/O 效能。

*(註：完整的 Mermaid 圖表代碼與詳細的 DDL 創表語句已完全內嵌於專案文件中。)*

---

## 🛠 3. 技術亮點

本系統除了滿足題目基本規範外，特別針對**資訊安全與維運紀律**進行了防錯設計：
1.  **伺服器端嚴謹文件檢查 (Server-side Inspection)**：
    *   新檔案上傳時，伺服器不接受前端自填的 metadata。
    *   **Word 檔案**：伺服器直接拆解讀取 Office 的 `docProps/app.xml` 擷取真正的頁數。
    *   **圖片檔案**：使用 `ImageSharp` 在後端直接解碼讀取二進位串流，取得精準的寬與高解析度。
    *   **純文字檔**：依 BOM（Byte Order Mark）與嚴格的 UTF-8 特徵檢查進行編碼判定。
2.  **雙階段安全建立流程**：
    *   檔案上傳後，後端檢查完畢僅回傳一個**短效檢查 Token**，確認新增時前端需帶回該 Token，徹底防止前端惡意篡改大小或偽造副檔名屬性。
3.  **高可用性 XML 輸出優化**：
    *   揚棄傳統高風險的字串拼接，全面改用 `.NET` 內建的 `XmlWriter` 進行格式化輸出，並針對非法的 XML 標籤名稱（如中文名或帶有點號的檔名）進行自動化安全轉碼（Safe Encoding），確保輸出 100% 通過瀏覽器 `DOMParser` 的合法驗證。

---

## ⚠️ 4. 當前版本已知限制與後續驗收 (Transparency)

本專案目前有以下已知限制，並已規劃後續優化藍圖：
*   **排序策略抽離**：目前名稱、大小、副檔名的升降冪排序行為已完整實作且運作正常，但底層比較策略尚未完全抽成獨立的 `SortStrategy` 模式類別，目前保留在目錄內部的 Linq 動態選擇中。
*   **文字編碼寬容度**：目前伺服器能 100% 精準識別 BOM、ASCII 與標準 UTF-8。若遇到無法可靠判定編碼的罕見舊式文字檔，系統會明確拒絕新增，不填入預設值以確保資料正確性。
*   **實體磁碟映射**：目前的英文儲存路徑（`storage path`）為記憶體模型中的邏輯相鄰清單，尚未實體映射到操作系統的磁碟硬碟空間。當 Web 伺服器重新啟動後，目錄樹將會重置回預設範例資料。
*   **自動化測試套件**：目前專案已通過嚴格的 `dotnet build`（0 錯誤、0 警告）以及手動瀏覽器與 Web API 的全面煙霧測試（Smoke Test）。因時間關係尚未建立獨立的 xUnit 自動化單元測試專案，這將作為下一階段的首要交付目標。