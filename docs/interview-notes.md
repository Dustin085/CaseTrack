# 面試素材

每一題都分成三層：**30 秒版本**（先講這個）→ **展開**（對方追問時講）→ **專案裡的證據**（指得出程式碼或測試）。

---

## 2026-10-07～08：整合測試、Repository、案件編號產生器、第一個 API

### 1. 你遇過最難抓的 bug 是什麼？

**30 秒版本**
補件功能的一個 bug：單元測試 41 個全綠，但連資料庫的整合測試大約一半機率失敗。原因是 Domain 用「集合的最後一個」代表最新的補件，這在記憶體裡成立，但從資料庫讀出來時，EF 產生的 SQL 沒有替子資料排序，順序是不固定的。我改成用業務意義「還沒補件的那一筆」去找，不再依賴順序，並且讓補件紀錄自己拒絕被補兩次，同樣的錯誤再發生時會當場報錯，而不是默默改壞歷史資料。

**展開**
- 發現的方式：先懷疑這個假設，再寫一個「從資料庫讀出來之後再補件」的整合測試，重複執行觀察到時好時壞。
- 印出 EF 實際的 SQL：`ORDER BY` 只排了案件的主鍵，子資料沒有排序。
- 我一度推測「有 `(CaseId, Sequence)` 索引，應該剛好會依順序回傳」，實測後推翻了：表很小時 SQL Server 直接掃描主表，主表依隨機 Guid 排列。**沒有 ORDER BY 就不能預測順序**，連推測都不可靠。
- 修正後連續執行 25 次都通過（修正前約一半失敗，連續 25 次都過的機率約三千萬分之一）。
- 防護層：`MarkSubmitted` 拒絕重複補件，丟 `InvalidOperationException`（代表程式 bug，不是使用者操作錯誤）。在舊的寫法下，它把「默默寫壞資料」變成「當場報錯」。

**學到的事**
- 測試全綠不代表沒有 bug，可能只是沒有觸發。
- 用業務意義表達規則，不要依賴實作細節（集合位置）。
- 每個物件守住自己的不變條件（縱深防禦）。

**證據**：`CaseTrack.Domain/Cases/Case.cs` 的 `SubmitSupplement`、`SupplementRequest.MarkSubmitted`；整合測試 `SubmitSupplement_OnReloadedCaseWithTwoRequests_MarksLatestRequest`。

---

### 2. 併發下不重複、每天重置的流水號怎麼做？

**30 秒版本**
用一張每天一列的計數表，取號時執行 `UPDATE ... SET LastValue = LastValue + 1 OUTPUT inserted.LastValue`。讀出、加一、寫回在同一個 SQL 陳述式裡完成，資料庫會鎖住那一列，所以不會有兩個請求拿到同一號。每天第一次取號時那一列還不存在，就 INSERT；如果兩個請求同時 INSERT，主鍵會擋下其中一個，被擋下的回頭重新 UPDATE。整合測試讓 50 個請求同時取號，結果剛好是 0001 到 0050。

**展開：比較過的做法**

| 做法 | 問題 |
|---|---|
| 查今天最大號再 +1 | check-then-act，同時查到同一號；靠唯一索引擋下再重試的話，50 個併發時最後一個要重試 49 次 |
| SQL SEQUENCE | 原子又快，但不會每天自動重置；半夜 `RESTART` 的瞬間有邊界問題 |
| **計數表 + 原子 UPDATE**（採用） | 每天第一次取號要處理 INSERT 競爭，用主鍵 + 重試解決，最多重試一次 |

**展開：細節**
- **更新遺失（lost update）**：如果改成「SELECT 出值 → C# 裡 +1 → UPDATE 寫回」，兩個請求都讀到 1、都寫回 2。我用這個錯誤寫法做反向驗證，併發測試每次都抓到重複的 `0002`。
- **台灣日期**：伺服器是 UTC，直接取日期的話，台灣早上 8 點前的案件會被算成前一天。用 `TimeProvider` 取得時間再轉 +08:00（台灣沒有日光節約時間）。`TimeProvider` 可以在測試裡換成固定時間，等同 Java 的 `Clock`。
- **跳號**：取號和存案件是不同交易，存檔失敗會跳號。陳情案件編號只需要唯一，所以業務上允許；如果是統一發票這種法規要求連續的號碼，就要放在同一個交易裡，代價是送件會排隊。
- **用錯誤代碼判斷撞號**：`catch (SqlException ex) when (ex.Number is 2627 or 2601)`，只有主鍵／唯一鍵重複才重試，其他錯誤照常往外拋。
- **SQL 參數化**：`SqlQuery<int>($"... {date}")` 看起來是字串插值，實際上 EF 會轉成 SQL 參數，沒有 SQL Injection 的問題。

**證據**：`CaseTrack.Infrastructure/Persistence/SqlCaseNumberGenerator.cs`；`CaseNumberGeneratorTests` 的 4 個測試（從 1 開始、隔天重置、台灣時區、50 個併發）。

---

### 3. 業務邏輯已經檢查了，為什麼還要資料庫約束？

**30 秒版本**
因為「先查有沒有、沒有才新增」中間有空檔，兩個請求可以同時通過檢查（check-then-act）。不管應用程式寫得多仔細，這個空檔都存在，開多台伺服器時程式裡的 lock 也沒用。資料庫的唯一約束是原子的，是唯一能真正保證的地方。所以兩層都要：業務邏輯負責預防、給使用者友善的訊息；資料庫負責保證。

**展開**
- 重複的值是**使用者輸入的**（例如帳號）：先查一次給友善訊息，撞到約束時回同樣的訊息。
- 重複的值是**系統產生的**（例如案件編號）：使用者無法處理，系統自己重試。
- 測試唯一約束時，要確認是「因為重複」而失敗：檢查 `SqlException.Number == 2601`，不能只看 `DbUpdateException`，因為欄位太長、NOT NULL 違反也都是 `DbUpdateException`。

**證據**：`CaseConfiguration` 的唯一索引；整合測試 `SaveChanges_WithDuplicateCaseNumber_ThrowsUniqueViolation`。

---

### 4. EF Core 的 DbContext 已經是 Repository 了，為什麼還要包一層？

**30 秒版本**
在我的分層裡，Application 不參考 Infrastructure，看不到 DbContext，所以 Application 定義 `ICaseRepository`，Infrastructure 實作，這是依賴反轉。但我不做通用的 `IRepository<T>`，而是每個聚合根一個，方法依業務需求設計。它最大的價值是「讀取案件時一定帶上完整的補件紀錄」，這件事只寫一次，用例不可能忘記 `Include`。

**展開**
- 通用 Repository 是反模式：只是把 EF 再包一層、`IQueryable` 會洩漏、連子物件都能繞過聚合根自己存取。
- 沒有 `Update` 方法：讀出來的物件被 DbContext 追蹤，修改後 `SaveChanges` 自動產生 UPDATE，等同 JPA 的 dirty checking。
- `SaveChanges` 拆成 `IUnitOfWork`：一個用例可能用到多個 Repository，由用例決定交易邊界，相當於 Spring 的 `@Transactional`。
- 另一派做法（Jason Taylor 範本）讓 Application 直接依賴 EF、暴露 `IApplicationDbContext`，比較省事；兩種都講得出取捨即可。

**DI 的經典陷阱**
`services.AddScoped<IUnitOfWork, CaseTrackDbContext>()` 會再建立**第二個** DbContext：Repository 加到 A，UnitOfWork 存的是 B，**不報錯但資料沒存進去**。要寫成 `services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CaseTrackDbContext>())`。我用錯誤的寫法做反向驗證，整合測試讀出來是 null。

**證據**：`Application/Abstractions/ICaseRepository.cs`、`IUnitOfWork.cs`；`Infrastructure/DependencyInjection.cs`；`CaseRepositoryTests`。

---

### 5. 你的整合測試怎麼設計？

**30 秒版本**
另外一個測試專案，連真的 SQL Server（LocalDB 裡的專用資料庫）。測試開始前刪掉資料庫、用 migration 重建，所以連 migration 本身也被驗證。每個測試的寫入、操作、驗證各用一個新的 DbContext，避免一級快取直接回傳記憶體裡的物件。每次加了測試都做反向驗證：故意改壞程式，確認測試真的會失敗。

**展開**
- **為什麼用 `MigrateAsync` 不用 `EnsureCreated`**：EF 9 之後，模型改了卻忘了產生 migration 時，`MigrateAsync` 會直接丟 `PendingModelChangesWarning`。我實際試過，三個測試全部失敗。`EnsureCreated` 會照目前模型建表，測試全過，到正式環境部署才發現。
- **一定要新的 DbContext**：同一個 DbContext 先存再讀，會從 identity map 拿回同一個物件，根本沒查資料庫（等同 JPA 一級快取，Java 要 `entityManager.clear()`）。
- **平行執行的坑**：兩個測試類別各自用 `IClassFixture`，xUnit 會平行執行，兩個 fixture 同時刪除重建同一個資料庫，出現「資料庫不存在」、死結等隨機錯誤。改用 Collection Fixture：共用一個 fixture、依序執行。
- **單元測試抓不到的問題**，這次整合測試抓到的：
  - 主鍵由應用程式產生時，EF 會把「讀出後新增的子物件」誤判成既有資料、送出 UPDATE（`DbUpdateConcurrencyException`），要設 `ValueGeneratedNever()`
  - 補件順序的 bug（第 1 題）
  - DI 註冊建立兩個 DbContext（第 4 題）
- 測試時間用 `TimeProvider`，正式環境用 `TimeProvider.System`，測試用固定時間的子類別。

**證據**：`CaseTrack.IntegrationTests/DatabaseFixture.cs`（含 `DatabaseCollection`）；`docs/ef-core-migrations.md`。

---

### 6. DI 的生命週期，DbContext 應該是哪一種？

**30 秒版本**
Scoped，每個 HTTP 請求一個。DbContext 不是執行緒安全的、會記住讀過的每一個物件、而且一個請求就是一個工作單位。

**展開**

| .NET | Spring | 意思 |
|---|---|---|
| Singleton | singleton | 整個程式一個 |
| Scoped | request scope | 每個請求一個 |
| Transient | prototype | 每次注入都新建 |

- **Captive dependency**：Singleton 不能直接依賴 Scoped，否則 Scoped 物件會活得跟整個程式一樣久。之後做逾期掃描的 `BackgroundService`（Singleton）時，要用 `IServiceScopeFactory` 每次建立新的 scope。
- 計數器產生器依賴 DbContext，所以也是 Scoped；`TimeProvider.System` 無狀態，Singleton。

---

### 7. API 設計上做了哪些決定？

**30 秒版本**
請求用專門的 DTO 並在 API 層驗證格式，用例用 Command 物件，回應用 DTO，不直接回傳 Domain 物件。新增成功回 201 和 Location 標頭，錯誤一律回 ProblemDetails 格式。

**展開**
- **三層驗證**：API 層檢查格式（必填、長度，回 400 告訴使用者哪個欄位錯）、Domain 檢查業務規則（狀態轉換）、資料庫是最後防線。
- **不回傳 Domain 物件**：API 格式會被 Domain 綁死；個資遮罩要在 DTO 層做（Domain 存完整資料）；private setter 和唯讀集合的序列化結果不可預期。等同 Java「不要直接回傳 JPA Entity」。
- **Request DTO 放在 Api 不放 Application**：`[Required]`、`[MaxLength]` 是 Web 層的關注點。
- **201 Created + Location**：HTTP 語意是「建立了新資源」，`Location` 指向它的網址，所以 GET 要一起做。
- **ProblemDetails（RFC 9457）**：400 和 404 格式一致，帶 `traceId` 可以對照伺服器 log。
- **不用 MediatR**：多一層間接、2025 年起改商業授權；用一般的 Handler 類別直接注入到 Action（`[FromServices]`）。

**踩過的坑**
record 的驗證標註寫成 `[property: Required]`，編譯會過，但 ASP.NET Core 執行時直接丟例外：record 是透過主建構子繫結的，標註必須放在建構子參數上。框架選擇丟例外而不是默默略過驗證，這是好的設計。

**證據**：`CaseTrack.Api/Contracts/SubmitCaseRequest.cs`、`Controllers/CasesController.cs`；`Application/Cases/`。

---

### 8. 政府系統常見的部署與資安考量（這幾天順便整理到的）

- **連線字串的密碼不進 git**：開發用 `appsettings.Development.json` + Windows 驗證（沒有密碼），正式環境用環境變數 `ConnectionStrings__CaseTrack`。
- **`TrustServerCertificate=True` 只能用在開發環境**：正式環境要有合法憑證，否則有中間人攻擊風險。
- **Migration 不在程式啟動時自動執行**：程式的資料庫帳號只給讀寫資料的權限，不給改 schema 的權限（權限分離）；schema 變更產生 idempotent SQL 腳本交給 DBA 審查執行。
- **SQL Injection**：手寫 SQL 一律用參數化（EF 的 `FormattableString`），不串接字串。
- **輸入長度限制**：每個欄位都有明確長度（政府規格書通常會寫），API 層和資料庫各擋一次。

---

## 2026-10-08：全域例外處理、承辦人處理流程、API 測試

### 9. 你的 API 怎麼處理錯誤？

**30 秒版本**
Controller 裡沒有任何 try/catch，全部交給 .NET 8 的 `IExceptionHandler` 統一處理。例外分三類：違反業務規則的 `DomainException` 回 409，找不到資源的 `NotFoundException` 回 404，其他所有例外一律回 500 而且不透露任何內部資訊，只給 `traceId` 讓我們對照 log。格式全部是 ProblemDetails（RFC 9457），跟框架自動產生的 400 一致。

**展開**
- **分類的依據是「使用者能不能自己解決」**：對已結案的案件按受理，使用者看到原因就知道怎麼辦（409 + 目前狀態、目標狀態）；程式 bug 告訴使用者細節也沒用，還會洩漏內部資訊（500）。
- **409 不是 400**：400 是請求格式錯，換時間送還是錯；409 是請求格式對，但跟資源目前的狀態衝突。
- 例外裡保留結構化的屬性（`CurrentStatus`、`TargetStatus`），放進 ProblemDetails 的 `Extensions`，前端不用解析英文訊息。
- 同一個 bug 示範分類的重要性：補件的 Handler 找不到案件時一開始丟 `InvalidOperationException`，結果回 500 而不是 404。**選錯例外型別，HTTP 狀態碼就會跟著錯。**
- **處理器裡要有「通用的 DomainException」回應**：一開始只處理兩種子類別、最後無條件 `return true`，新的子類別會變成空白的 200 回應，比 500 更糟，因為沒人會發現。
- 被 `IExceptionHandler` 處理掉的例外，.NET 10 預設不記錄成錯誤，所以 409 要自己記一筆 Warning（代表前端可能沒擋住不合法的按鈕）。404 太常見，不記。
- `UseExceptionHandler()` 放在 middleware 最外層；少了 `AddProblemDetails()` 的話 API 在**啟動時**就會失敗（好的設計：早失敗）。

**證據**：`CaseTrack.Api/ExceptionHandling/`；API 測試 `ReviewCase_WhenClosed_ReturnsConflictWithStatuses`。

---

### 10. 改變狀態的 API 怎麼設計？為什麼不用 PATCH？

**30 秒版本**
每個業務動作一個端點：`POST /api/cases/{id}/review`、`/close`、`/reject`、`/supplement-requests`、`/supplement-submission`。因為 Domain 是充血模型，狀態只能透過業務方法改變，如果開一個 `PATCH { "status": "Closed" }`，等於在 API 層開了一個直接改狀態的後門。

**展開**
- 每個動作需要的參數不同（退件要原因、受理不用），PATCH 會把它們混在一起。
- 每個端點對應一個 Domain 方法，之後可以分別設定權限（例如只有主管能結案）、稽核紀錄看得到「做了什麼動作」。
- 用 POST 因為這是**動作**，不是修改資源的某個欄位。成功回 204。
- 每個用例一個 Handler（vertical slice），只注入自己需要的依賴，避免 Spring 式的 `CaseService` 變成依賴越來越多的上帝類別。

---

### 11. 單元測試、整合測試、API 測試差在哪？

**30 秒版本**
單元測試測 Domain 的業務規則，不碰資料庫；整合測試連真的 SQL Server，測 EF 對應、SQL、DI；API 測試用 `WebApplicationFactory` 在記憶體裡啟動整個 API，用 HttpClient 打請求，路由、模型繫結、驗證、例外處理全部都會經過。越上層越慢，數量越少。

**展開：每一層實際抓到過的 bug**

| 層 | 抓到的 bug |
|---|---|
| 單元測試 | 狀態機的規則、補件標記到錯的那一筆（記憶體情境） |
| 整合測試 | 補件順序（資料庫不保證順序）、主鍵被誤判成 UPDATE、併發撞號、DI 建了兩個 DbContext |
| API 測試 | record 驗證標註加 `property:` 導致每個 POST 都 500、漏註冊 Handler／例外處理器、DTO 漏了退件原因 |

**API 測試的陷阱**
- **只檢查狀態碼不夠**：路由對不到時也是 404（內容是空的），要檢查 `application/problem+json` 才知道是我們的處理器回的。我用「把路由打錯字」做反向驗證，只檢查狀態碼的測試照樣通過。
- **站在外部使用者的角度**：請求用匿名型別、狀態比對字串 `"Closed"` 而不是 `CaseStatus.Closed.ToString()`，否則 enum 改名時 API 合約變了測試卻不會失敗。
- **用 Location 去 GET**，才證明 201 的 Location 真的能用。
- 測試連線字串用 `UseSetting` 覆蓋：.NET 6 之後在 `Program.cs` 直接讀設定，`ConfigureAppConfiguration` 加入的設定太晚生效。

**證據**：`CaseTrack.IntegrationTests/Api/`。

---

### 12. 遇過最麻煩的環境問題？怎麼排查？

**30 秒版本**
LocalDB 反覆啟動失敗。我從錯誤記錄看到 SQL Server 其實有啟動成功，但 Windows 事件記錄顯示 LocalDB 的管理程式等不到啟動完成的訊號，就把狀態記成已停止，留下一個它管不到的程序，之後每次啟動都因為檔案被佔用而失敗。用全新的執行個體測試正常，所以判斷是那個執行個體的問題；重建時又發現有別的程式（推測是 VS）在幾秒內觸發了自動啟動。最後讓開發工具和我的驗證用不同的執行個體，長期打算改用 Docker。

**展開**
- 排查順序：錯誤訊息 → 執行個體狀態 vs 實際程序 → SQL Server 的 error.log → Windows 事件記錄 → 用全新的執行個體做對照組。
- 處理方式從強制結束程序改成透過 named pipe 連進去執行 `SHUTDOWN`，正常關閉。
- 所有 LocalDB 執行個體的 .mdf 預設在同一個資料夾，資料庫名稱不能重複。
- 測試的連線字串改成可以用環境變數覆蓋，換 Docker 或上 CI 時測試程式碼不用改。
