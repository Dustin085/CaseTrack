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

---

## 2026-10-12～14：樂觀併發、逾期排程、稽核軌跡、陳情人資料與個資遮罩

### 13. 兩個人同時修改同一個案件，怎麼處理？

**30 秒版本**
用樂觀併發控制。`Cases` 表有一個 SQL Server 的 `rowversion` 欄位，每次 UPDATE 資料庫都會自動改它。EF Core 會把讀出時的值放進 UPDATE 的 WHERE 條件，如果影響 0 筆，代表讀出之後已被別人改過，EF 丟出 `DbUpdateConcurrencyException`。我在 Unit of Work 裡把它轉成 Application 層的 `ConcurrencyConflictException`，API 回 409，請使用者重新讀取。

**展開**
- **問題是「遺失更新」**：A、B 同時讀出審核中的案件，A 退件、B 結案，沒有控制的話後存的會蓋掉先存的，而且沒有任何人知道。
- **為什麼用樂觀鎖不用悲觀鎖**：陳情案件同時被兩個人改的機率很低，悲觀鎖會讓每次讀取都鎖住資料，代價太高。
- **`RowVersion` 用影子屬性**：這是資料庫的技術細節，Domain 不需要知道，`Case` 類別上沒有這個屬性。
- **例外轉換放在 `CaseTrackDbContext` 明確實作的 `IUnitOfWork.SaveChangesAsync`**：Application 層不認識 EF 的例外型別，依賴方向不會被破壞。
- 排程遇到併發衝突時（例如民眾剛好在那一刻補件），**只略過那一筆**，下一輪再判斷。

**證據**：`CasePersistenceTests.SaveChanges_WhenConcurrentUpdateOccurs_ThrowsConcurrencyConflict`。

---

### 14. 定時排程怎麼設計？（逾期 14 天未補件自動退件）

**30 秒版本**
用 `BackgroundService` + `PeriodicTimer` 定時掃描。先查出逾期案件的 Id，再**每個案件開一個新的 DI scope**，用跟 API 相同的 Handler 處理。Handler 會**重新讀取並再判斷一次**是否逾期，因為從查詢到處理之間，民眾可能剛好補件了。

**展開**
- **check-then-act（TOCTOU）**：查詢時逾期，不代表處理時還逾期。所以 Handler 讀出案件後再呼叫 `IsSupplementOverdue(now)`，不是的話就安靜地回傳 `false`；真的同時發生時，還有樂觀鎖擋住。
- **為什麼每個案件一個 scope**：DbContext 會追蹤它讀出的所有 entity。如果整批共用一個 DbContext，某一筆存檔失敗後還留在變更追蹤器裡，下一筆 `SaveChanges` 會把它再存一次，**一筆壞掉的資料害整批都存不進去**。
- **BackgroundService 是 Singleton**，不能直接注入 Scoped 的 DbContext（會在啟動時得到 captive dependency 的錯誤），要注入 `IServiceScopeFactory` 自己建立 scope。
- **迴圈裡要接住例外**：BackgroundService 裡未處理的例外會讓整個應用程式停止（.NET 6 之後的預設行為，`BackgroundServiceExceptionBehavior.StopHost`）。但要排除 `OperationCanceledException`，那是正常關機。
- **時間可以注入**：`TimeProvider` + `PeriodicTimer(interval, timeProvider)`，測試裡用固定的時間，不用真的等 14 天。
- **規則寫兩次的風險**：逾期條件在 Domain（`IsSupplementOverdue`）和 SQL 查詢裡各一份，因為 EF 沒辦法翻譯自訂的 C# 方法。用整合測試確認兩者一致，並用測試把「14 天」釘死。
- 間隔、批次大小用 Options 模式設定（≈ Spring 的 `@ConfigurationProperties`），開發環境 30 秒、正式環境 1 小時。

**證據**：`OverdueSupplementScanner`、`RejectOverdueCaseHandler`；`OverdueSupplementTests.HandleAsync_WhenSupplementAlreadySubmitted_LeavesCaseUnchanged`。

---

### 15. 稽核軌跡怎麼做？怎麼確保稽核紀錄跟實際資料一致？

**30 秒版本**
用 EF Core 的 `SaveChangesInterceptor`，在 **`SavingChanges`**（存檔之前）讀取變更追蹤器，把每筆新增、修改、刪除轉成 `AuditEntry`，加進**同一個 DbContext**。這樣稽核紀錄和業務資料在**同一個交易**裡寫入，業務資料沒寫進去，稽核也不會寫進去。我用一個併發衝突的測試證明這點，並做了反向驗證。

**展開**
- **記什麼**：誰（`ICurrentUser`，排程是 `system`）、什麼時候（UTC）、哪一筆、新增／修改／刪除、改了哪些欄位的前後值（JSON）。修改只記錄真的改過的欄位。
- **為什麼是 `SavingChanges` 不是 `SavedChanges`**：`SavedChanges` 時業務資料已經 commit，這時加的稽核紀錄要再存一次，而且是另一個交易。
- **EF 的 `SaveChanges` 本身就是一個交易**，所以不需要 Spring 的 `@Transactional`；每個用例只呼叫一次 `SaveChanges`，一個用例就是一個交易。
- **攔截器的限制（要主動說）**：只看得到經過變更追蹤器的修改。`ExecuteUpdate`、直接執行 SQL、Dapper 都會繞過；需要完整涵蓋時要用 SQL Server 的 Temporal Table 或資料庫層的稽核。
- **同步和非同步是兩個獨立的覆寫點**：我一開始只覆寫了 `SavingChangesAsync`，複習時發現呼叫同步的 `SaveChanges()` 會**靜靜地跳過稽核**。補上覆寫和測試，反向驗證確認測試抓得到。
- **為什麼不用 Domain Events**：攔截器不需要在每個業務方法裡發事件，不會漏。Domain Events 留給「通知民眾」這種需要表達業務意義的場景。
- 「誰看了個資」是另一個需求（查詢紀錄），攔截器只記錄修改，不記錄讀取。

**證據**：`AuditInterceptor`；`AuditInterceptorTests`：`SaveChanges_ConcurrencyConflict_DoesNotAddAuditEntry`（反向驗證：改成在交易外寫入稽核，測試失敗）、`SaveChanges_StartReview_RecordsOnlyStatusChange`、`SaveChanges_Synchronous_AddsAuditEntry`。

---

### 16. 你怎麼處理個資？

**30 秒版本**
三個層次。**蒐集**：依最小蒐集原則，陳情只需要聯絡得到人，所以收姓名和電話或 Email，不收身分證字號。**顯示**：API 回傳時遮罩（`王*明`、`09******78`）。**稽核**：稽核表也是個資外洩的管道，所以個資欄位在稽核紀錄裡也只存遮罩後的值。

**展開**
- **選填的個資欄位是最差的設計**：大部分人不填，業務價值接近零；但只要有一個人填了，遮罩、存取紀錄、加密、外洩通報的成本一樣都少不了。原則是「業務上需要就必填，不需要就不收」。
- **怎麼標記哪些欄位是個資**：在 EF 的 Configuration 加上自訂的 `.IsPersonalData()`（EF 的 annotation），稽核攔截器從模型讀出標記。標記跟欄位對應寫在同一個地方，新增欄位時不容易漏；不影響資料表結構，不需要 migration。
- **遮罩規則放在 Application**：稽核（Infrastructure）和 API 回應都要用，兩者共同依賴的是 Application，規則只寫一份。
- **API 的遮罩放在 Handler，不是 Controller**：要不要遮罩取決於「誰在看」，承辦人一定要看到完整電話才能聯絡民眾。這是業務規則加上授權，之後做授權時會改成依角色決定，並寫入個資查詢紀錄。目前還沒有登入，所以一律遮罩，比較安全的預設值。
- **罕見字**：.NET 字串是 UTF-16，罕見字（例如 `𠀀`）佔兩個 `char`，用 `string.Length` 切會變亂碼，所以用 `StringInfo` 以文字元素計算。戶政資料裡罕見字很常見。
- **測試不依賴 JSON 結構**：`Assert.DoesNotContain("0912345678", body)`，不管格式怎麼改，完整的電話出現在任何地方測試都會失敗。
- 測試抓到的 bug：`Convert.ToString(null)` 回傳 `""` 不是 `null`，「沒填」被記成「填了空字串」。

**證據**：`PersonalDataMasker`、`PersonalDataAnnotations`；`AuditInterceptorTests.SaveChanges_NewCase_RecordsMaskedPetitionerFieldsWithPrefix`、`CaseWorkflowApiTests.GetCase_WithPetitioner_ReturnsMaskedPetitioner`。

---

### 17. 值物件在 EF Core 裡怎麼對應？遇過什麼問題？

**30 秒版本**
陳情人資料是值物件（`sealed record Petitioner`），代表送件當時的快照，用 EF Core 的 `ComplexProperty` 攤平存在 `Cases` 表裡（`Petitioner_Name`…），≈ JPA 的 `@Embedded`。我實際比較過 `OwnsOne`：Owned Type 其實是有隱藏主鍵的 entity，整個換掉值物件時會被追蹤成「刪除舊的 + 新增新的」，稽核紀錄會看不懂；`ComplexProperty` 則是「案件的電話被修改」，語意才正確。

**展開**
- **為什麼是值物件不是獨立的資料表**：跟訂單上的收件地址一樣，民眾之後換電話，舊案件記錄的仍是當時的聯絡方式。等民眾有帳號時，才會是「案件指向會員 + 案件上的聯絡資料快照」。
- **不合法的值物件建立不出來**：驗證寫在建構子；屬性只有 `get`、沒有 `init`，因為 record 的 `with` 會複製物件但**不經過建構子**，有 `init` 就能繞過驗證。
- **坑 1：EF 不能用建構子參數綁定 Complex Type**，`Case` 要加一個 `private` 的無參數建構子給 EF 使用（≈ JPA 要求的 `protected` 無參數建構子）。
- **坑 2：舊資料讀不出來**：EF 讀資料時會優先呼叫參數相符的建構子，驗證也跟著執行；migration 之前的舊案件姓名是空字串，一讀就拋例外。解法是 `Petitioner` 也加 EF 專用的無參數建構子。原則：**驗證只在建立時做，讀取時信任資料庫**，因為規則會隨時間變嚴格，舊資料不符合新規則也必須讀得出來。我用直接寫 SQL 的方式模擬 migration 之前的資料，寫成測試。
- **坑 3：稽核攔截器漏掉 Complex Type**：它的欄位在 `entry.ComplexProperties`，不在 `entry.Properties`，不處理的話陳情人資料完全不會被稽核，也不會報錯。欄位名稱加前綴 `Petitioner.Phone` 避免撞名。

**證據**：`CaseConfiguration`（`ComplexProperty` 的註解）、`LegacyDataTests.GetCase_PetitionerViolatesCurrentRules_StillLoads`。

---

### 18. 什麼時候回 400、409、500？

**30 秒版本**
看錯誤是從哪裡來的：**使用者送來的資料不合法** → 400，改了再送就會成功；**資料沒問題，但案件目前的狀態不允許** → 409，例如已結案不能退件；**系統內部的問題** → 500。Domain 的例外分成兩類，`DomainValidationException` 對應 400，其他 `DomainException` 對應 409。

**展開**
- **Domain 只負責分類，不知道 HTTP**：不像 Spring 在例外上標 `@ResponseStatus`，我讓 Domain 表達「這是什麼性質的錯誤」，由 Api 的例外處理器決定對應哪個狀態碼。
- 處理器依**基底類別**判斷，不逐一列出例外型別，所以 Domain 新增驗證類的例外時，Api 不用改。
- 「電話與 Email 至少一項」跨兩個欄位，單一欄位的驗證標註表達不了，所以放在 Domain，不在 API 層再寫一份。取捨：這種 400 沒有欄位層級的 `errors`，前端會收到兩種格式的 400。
- 「補件時間早於要求補件時間」不是 400：時間是伺服器產生的，使用者怎麼改都沒用。
- **為什麼不用 Result Pattern**：失敗是流程的正常結果時用回傳值（例如排程的 Handler 回傳 `bool`），代表程式或資料不合法時用例外。

**證據**：`DomainValidationException`、`DomainExceptionHandler`；`CaseWorkflowApiTests.SubmitCase_WithoutContact_ReturnsBadRequestWithDetail`。

---

## 2026-10-15：JWT 身分驗證、單位與分派

### 19. 你的系統怎麼做登入？為什麼選 JWT？

**30 秒版本**
API 本身不管帳號密碼，只負責驗證 token（OAuth 的 Resource Server）。確認「你是誰」交給外部的身分提供者（IdP），政府機關通常是 AD 或單一登入；開發環境用一個只存在於 Development 的 `/dev/token` 端點代替。選 JWT 是因為前端會另外部署（不同來源）、測試和 Swagger 好用，而且正式環境接 IdP 時，IdP 發的本來就是 token。

**展開**
- **JWT 和 Cookie 的取捨（要主動講）**：HttpOnly cookie 不會被 XSS 讀走，但要防 CSRF；JWT 沒有 CSRF 問題，但前端要自己保存，放 `localStorage` 會被 XSS 偷走。如果前端和 API 在同一個網域，正式的政府系統我會比較傾向 cookie 或 BFF。
- **JWT 的限制**：登出或停權之後，token 在效期內仍然有效。我的做法是短效期（30 分鐘）、前端放記憶體，立即失效列為已知限制，之後可以加 refresh token 或黑名單。
- **ASP.NET Core 的 Cookie 驗證跟 Spring 不一樣**：Spring 預設是 `JSESSIONID` + 伺服器端 session；ASP.NET Core 是把加密後的整張票證放在 cookie 裡，預設無狀態。多台伺服器要共用 Data Protection 金鑰，不然重啟或換一台就全部被登出。
- **JWT 只有簽章、沒有加密**：payload 任何人都能解開來看，所以 token 裡只放使用者 Id、名稱、角色、單位，絕對不放個資。
- **為什麼不自己管密碼**：離職、調職只要在 IdP 停用一次，所有系統同時失效；系統本身不存密碼，就不會是密碼外洩的來源。

**證據**：`Program.cs` 的 `AddJwtBearer`、`Authentication/DevTokenEndpoint.cs`。

---

### 20. 驗證 JWT 時要檢查什麼？遇過什麼坑？

**30 秒版本**
簽章、發行者（issuer）、對象（audience）、有效期限。最大的坑是 `MapInboundClaims`：預設會把 `role`、`sub` 改名成微軟舊格式的長名稱，結果 `IsInRole` 永遠回傳 false，所有角色檢查都會靜靜地失敗、一直回 403。我在 scratchpad 先實驗確認，再設成 `false`。

**展開**
- **Audience 的意義**：同一個 IdP 發給別的系統的 token，不能拿來呼叫我的 API。我有測試：別的 audience 的 token → 401。
- **ClockSkew 預設 5 分鐘**：token 過期後還會被接受 5 分鐘，對 30 分鐘效期太長，改成 1 分鐘，並用「過期 2 分鐘的 token → 401」釘住。
- **金鑰管理**：開發環境放 User Secrets（專案資料夾外的 JSON 檔，不會被 commit；注意它沒有加密），正式環境用環境變數。ASP.NET Core 的設定是疊加的：appsettings → 環境設定 → User Secrets → 環境變數，後面覆蓋前面，跟 Spring Boot 的 profile 很像。
- **設定缺值要早失敗，而且訊息要看得懂**：我原本以為 `required` 會擋住，實際上設定檔綁定用反射建立物件，會繞過 `required`，啟動時只看到 `ArgumentNullException (Parameter 's')`。改成明確檢查金鑰是否存在、長度是否至少 32 位元組，錯誤訊息直接寫怎麼修。
- **中介軟體的順序**：`UseAuthentication` 一定要在 `UseAuthorization` 之前，反過來所有人都會被當成匿名。
- **正式環境用 RS256**：IdP 用私鑰簽、API 只拿公鑰驗證，API 被入侵也簽不出 token；開發環境的 HS256 對稱金鑰只是替代品，切換時只改 `Program.cs`。

**證據**：`AuthenticationApiTests`（過期、別的金鑰、別的 audience、沒有 token 都是 401，搭配一個合法 token 的正面對照）；反向驗證：改回 `MapInboundClaims = true`、ClockSkew 預設、不檢查 audience，各自讓對應的測試失敗。

---

### 21. 開發環境怎麼拿 token？怎麼確保那個後門不會出現在正式環境？

**30 秒版本**
`/dev/token` 不驗證密碼、指定誰就發誰，等於一個後門，所以用 Minimal API 寫在 `if (app.Environment.IsDevelopment())` 裡面：正式環境根本不會註冊這個路由。我用 `WebApplicationFactory` 以 Production 環境啟動 API，寫測試證明它回 404，並搭配「開發環境回 200」的正面對照，避免路徑打錯也讓測試通過。

**展開**
- **為什麼不用 Controller**：Controller 是自動掃描註冊的，不管什麼環境都存在，只能在裡面自己擋；哪天有人改壞那個 `if`，就變成正式環境的漏洞。
- **發 token 時就檢查角色**：角色打錯字的 token 照樣發得出來，但之後每個請求都莫名其妙 403，很難查。
- **測試不依賴開發者電腦的設定**：測試直接用寫死的「測試專用金鑰」簽 token（`UseSetting` 覆蓋 User Secrets），換電腦或上 CI 結果都一樣。我是在發現「API 測試其實偷偷讀到我電腦上的 User Secrets」之後才改的。
- **正面對照的重要性**：只檢查「401」的測試，在 API 什麼 token 都不接受時也會通過；只檢查「404」的測試，在路徑打錯時也會通過。

**證據**：`AuthenticationApiTests.DevToken_InProduction_ReturnsNotFound`、`DevToken_InDevelopment_ReturnsToken`；反向驗證：把 `if` 拿掉，Production 的測試失敗。

---

### 22. 身分驗證和授權差在哪？你的授權怎麼設計？

**30 秒版本**
身分驗證是「你是誰」（失敗 401），授權是「你能做什麼」（失敗 403）。授權分兩層：第一層看角色能不能做這個動作（Policy），第二層看能不能碰這一筆資料（承辦人只能看本單位的案件）。看不到的資料回 404 而不是 403，因為 403 等於告訴對方「這筆資料存在」。

**展開**
- **預設拒絕**：整個 Controller 加 `[Authorize]`，例外的端點（民眾送件）個別加 `[AllowAnonymous]`。新端點忘記設定，結果是多一道登入，而不是少一道。
- **角色從哪裡來**：練習專案放在 token 的 claims 裡；實務上更常見的是 IdP 只管身分、系統自己有權限表（搭配權限申請和定期盤點）。我用 `ICurrentUser` 把來源隔離，改成查權限表只要換一個類別。
- **最小權限**：「管理者」維護系統設定，不代表有業務需要看民眾個資，所以管理者不能看案件內容。
- **資料列過濾放在哪裡**：比較過每個 Handler 自己檢查、Repository 強制傳入範圍、EF Global Query Filter。選 Repository 強制傳入，因為忘記傳就編譯不過、看 Handler 就知道套用了什麼權限、之後用 Dapper 查詢也能沿用；Global Query Filter 是隱形的，而且 Dapper 和直接執行的 SQL 不會套用。
- **稽核記錄使用者 Id（`sub`）**：姓名會重複、會改，不能用來追查責任。有登入卻沒有 `sub` 時直接拋例外，不默默記成 anonymous；反向驗證時這個設計讓 `MapInboundClaims` 的錯誤立刻被抓到。

**證據**：`CasesController`、`HttpCurrentUser`、`HttpCurrentUserTests`；資料列層級過濾的實作見第 24、25 題。

---

### 23. 案件分派到單位，資料模型怎麼設計？

**30 秒版本**
`Case` 只存 `DepartmentId`，沒有 `Department` 導覽屬性，因為兩者是不同的聚合，聚合之間只用 Id 參照。但資料庫仍然用外鍵保證單位存在：EF 用 `HasOne<Department>().WithMany()` 設定沒有導覽屬性的外鍵，刪除單位時 Restrict，避免案件悄悄變回「待分派」。

**展開**
- **Domain 規則**：只有「已送出」可以分派、可以重新分派；受理之前一定要分派。檢查順序是先狀態、再分派，先回報最根本的原因，有測試釘住順序。
- **代理鍵 vs 自然鍵**：單位用 Guid 主鍵 + 唯一的代碼，代碼要改時案件不用跟著改；代價是 token、log 裡看到的是 Guid。
- **預先資料**：`HasData` 用匿名物件指定固定的 Guid，Domain 不需要為了種子資料開一個能指定 Id 的建構子。
- **單位不存在回 400 不是 404**：404 是網址裡的資源（案件）不存在；單位在請求內容裡，是「送來的資料有問題」。Handler 先查單位是否存在，回 `ValidationProblemDetails`，跟 `[ApiController]` 的格式一致；外鍵是最後一道防線（查詢和存檔之間被刪掉）。
- **值型別的必填欄位要用 `Guid?`**：用 `Guid` 的話，沒填會變成 `Guid.Empty`，繞過 `[Required]`。我原本以為會變成 500，反向驗證時發現其實是 400 但訊息錯成「單位不存在」，測試也因此改成同時檢查錯誤訊息。
- **換型別時原本的檢查不會跟著換**：單位 Id 從字串改成 Guid 之後，要另外擋 `Guid.Empty`。
- **反向驗證要精確**：第一次拿掉 EF 的外鍵設定，模型和 migration 對不上，所有測試一起失敗，證明不了什麼；改成只拿掉 migration 裡建立外鍵的那一段，只有外鍵的測試失敗。

**證據**：`CaseConfiguration`、`DepartmentConfiguration`；`CasePersistenceTests.SaveChanges_AssignedToNonexistentDepartment_ThrowsForeignKeyViolation`（SQL 錯誤碼 547）、`AssignmentApiTests`。

---

## 2026-10-16：資料列層級授權、不予受理、個資查詢紀錄

### 24. 「承辦人只能看自己單位的案件」，你怎麼實作？

**30 秒版本**
用一個 `CaseAccessScope` 型別描述「這個人能看哪些案件」：全部、某個單位、或什麼都沒有。Handler 依照目前使用者的角色算出範圍，Repository 把範圍轉成 SQL 的 `WHERE`。Repository 刻意沒有「不帶範圍」的查詢方法，所以忘記傳範圍就編譯不過。不在範圍內的案件查不到，對外回 404，跟不存在一樣。

**展開**
- **為什麼不直接傳 `Guid? departmentId`**：`null` 同時會代表「全部」（分派人員）和「沒有」（管理者），Repository 分辨不出來，只要判斷錯一次，管理者就看得到所有個資。獨立的型別讓不合法的狀態無法表示。
- **規則放在 Application，翻譯放在 Infrastructure**：「誰能看哪些案件」是業務規則；Repository 只負責把範圍轉成 SQL，不需要知道角色。之後的列表、搜尋、報表都用同一個範圍。
- **處處預設拒絕**：`For()` 最後一行是 `None`（新增角色忘了處理 → 什麼都看不到）；承辦人沒有單位 → `None`；enum 明確寫 `None = 0`（沒設定的值是 0）；Repository 的 `switch` 把每一種範圍寫出來，不認識的種類拋例外，不能寫成「Department 才過濾、其他不過濾」。
- **明確繞過的地方要搜得到**：逾期排程和民眾補件（已知漏洞）明確寫 `CaseAccessScope.All` 並加註解，搜尋它就找得到所有繞過單位限制的地方。（10/19 民眾改成要登入後，補件改用 `ForPetitioner`，漏洞修掉，見第 31 題）
- **跟 EF Global Query Filter 比較**：Global Query Filter 不會忘記，但它是隱形的，而且 Dapper、直接執行的 SQL 不會套用。
- **和 Spring 的差別**：Spring 常見的寫法是每個 service 方法裡寫 `if (!dept.equals(...)) throw`，忘了寫沒有任何東西提醒；這裡是忘了傳就編譯不過。

**證據**：`CaseAccessScope`、`CaseRepository.GetByIdAsync`；`CaseAccessScopeTests`、`CaseRepositoryTests`（未分派的案件用單位範圍查不到，驗證 SQL 的 `NULL = @id`）、`AuthorizationApiTests`；反向驗證：拿掉 Repository 的單位條件，Repository 和 API 的測試都失敗。

---

### 25. Policy 用角色，資料過濾用範圍，為什麼要兩層？

**30 秒版本**
兩層回答不同的問題。第 1 層 Policy 問「這種人能不能做這個動作」，只看 token 就能決定，失敗回 403；第 2 層範圍問「能碰哪幾筆資料」，必須跟每一筆資料的單位比對，失敗回 404。只有第 1 層的話，工務局的承辦人能處理交通局的案件；只有第 2 層的話，分派人員看得到所有案件，就也能受理所有案件。

**展開**
- 類比：Policy 是門禁卡（能不能進這層樓），範圍是能打開哪幾個櫃子。
- Policy 的名稱表達意圖（`CaseReader`、`CaseHandler`、`CaseDispatcher`），角色清單只寫在 `Program.cs`；類別上保留 `[Authorize]`，新端點忘了加 Policy 至少還要登入。
- 我用反向驗證證明兩層都需要：把受理端點的 Policy 拿掉，分派人員的範圍是「全部」，第 2 層擋不住他，只有「分派人員受理 → 403」的測試失敗。
- **規則同時跟角色和資料狀態有關時**（分派人員只能退「已送出」的案件），Policy 做不到。我沒有把 `if` 塞回 Handler，而是把它拆成兩個業務動作：承辦人的「退件」和分派人員的「不予受理」，狀態規則放進 Domain。框架官方的做法是以資源為基礎的授權（`IAuthorizationService.AuthorizeAsync(user, resource, requirement)`），規則多的時候再用。
- 註冊順序的坑：`builder.Services.Add...` 一定要在 `builder.Build()` 之前，我放錯過一次，API 一啟動就失敗。

**證據**：`Policies`、`Program.cs` 的 `AddAuthorizationBuilder`、`Case.Dismiss`；`AuthorizationApiTests`。

---

### 26. 為什麼系統管理者不能看案件內容？

**30 秒版本**
最小權限：管理者管的是系統（帳號、單位、設定），工作上不需要知道民眾的電話。管理者帳號是攻擊者的第一目標，權限分開之後，管理者帳號外洩也不等於所有個資外洩。個資法也要求個資只能在特定目的的必要範圍內使用。

**展開**
- 資安稽核常檢查「系統管理者能不能看到業務資料」。
- 應用程式的權限擋不住 DBA 直接查資料庫，要搭配資料庫稽核、欄位加密、職責分離。
- 管理者需要查問題時，用「緊急存取」：臨時開權限、必須填理由、特別記錄、事後審查，而不是平常就給權限。
- 測試資料要讓錯誤現形：管理者的測試帳號刻意給他一個單位。如果程式把管理者誤當成承辦人處理，沒有單位的管理者剛好也是「看不到」，測試會抓不到。

**證據**：`CaseAccessScopeTests.For_AdministratorWithDepartment_ReturnsNone`、`AuthorizationApiTests.GetCase_ByAdministrator_ReturnsForbidden`。

---

### 27. 承辦人怎麼看到完整的個資？怎麼留下查詢紀錄？

**30 秒版本**
預設一律遮罩，另外一個端點 `GET /api/cases/{id}/petitioner` 回傳完整資料，只有本單位的承辦人和主管能呼叫。每次呼叫都先寫入查詢紀錄（誰、何時、哪個案件）並存檔成功，才回傳資料；存檔失敗就拿不到個資。回應加上 `Cache-Control: no-store`，避免被瀏覽器或 proxy 快取。

**展開**
- **為什麼不在一般查詢直接給完整資料**：大部分時候只需要看陳情內容；預設遮罩，螢幕被看到、被截圖也不會外洩。而且每打開一次案件就記一筆的話，紀錄多到沒人看得完；明確要求才顯示，紀錄才有意義。
- **查詢紀錄和稽核紀錄分開存**：稽核記錄的是修改，由攔截器自動產生；查詢紀錄由 Handler 明確寫入。常見的查詢也不同：「某個人最近看了哪些民眾的資料」（調查濫用），保存期限也可能不同。紀錄本身不再稽核。
- **用假物件測試「存檔失敗」**：整合測試很難讓資料庫在那一刻失敗，用假的 `IUnitOfWork` 證明存檔失敗時 Handler 拋出例外、不回傳資料。
- **一個因為錯誤原因通過的測試**：我一開始用稽核紀錄的 `EntityId` 找查詢紀錄，但查詢紀錄的主鍵是 IDENTITY，攔截器在存檔前執行，拿到的是 EF 的暫時值（負數），永遠對不上，測試永遠通過。反向驗證時才發現，改用 `Changes` 裡的案件 Id 找。

**證據**：`GetPetitionerHandler`、`PersonalDataAccessEntry`；`PersonalDataApiTests`、`GetPetitionerHandlerTests`。

---

## 2026-10-19：送件人與結案回覆、案件列表、套件弱點、民眾登入

### 28. 列表 API 的分頁和篩選怎麼做？踩過什麼坑？

**30 秒版本**
`GET /api/cases` 支援狀態、關鍵字篩選和頁碼分頁（預設 20 筆、最多 100 筆）。查詢一律先套用使用者的範圍，再篩選、算總筆數、排序、分頁，最後在 SQL 裡直接投影成摘要，不讀出陳情人的欄位。我寫了一組專門讓錯誤現形的測試，連「SQL 有沒有選到個資欄位」都檢查。

**展開**
- **總筆數也要套用範圍**：從整張表算的話，數字是錯的，而且會洩漏全系統的案件數。
- **關鍵字是「主旨或案件編號」**：連續寫兩個 `Where` 是 AND，關鍵字只出現在主旨的案件就找不到了。測試兩個方向都測。
- **LIKE 的萬用字元**：EF 的 `Contains` 會跳脫 `%`、`_`，自己用 `EF.Functions.Like` 拼字串就要自己跳脫，否則搜尋「%」會列出全部。
- **穩定排序**：只依送件時間排序的話，時間相同的案件在不同頁的順序不固定，翻頁時會重複或漏掉；加上案件編號當第二排序。測試刻意讓 5 筆案件的送件時間完全相同。
- **投影要寫在 SQL 能翻譯的地方**：`Select` 裡呼叫自訂方法，EF 翻譯不了，會把整個 `Case`（含陳情人）讀進記憶體再轉換。回傳的資料看起來一樣，只有看 SQL 才發現。我用 `DbCommandInterceptor` 記錄實際的 SQL，斷言裡面沒有 `Petitioner_` 欄位。
- **頁碼分頁的限制**：資料很多時，`OFFSET` 越後面越慢，翻頁時有新資料也會讓內容位移。真的有這個需求再換成 keyset（用上一頁最後一筆當條件）。
- **範圍是「什麼都沒有」時直接回傳空結果**，不送查詢到資料庫。

**證據**：`CaseRepository.ListAsync`、`ListCasesRequest`；`CaseListTests`（12 個，含 `SqlRecorder`）、`AuthorizationApiTests` 的列表測試；反向驗證：放回 6 種 bug，每一種都有測試失敗。

---

### 29. enum 在 API 裡輸出成名稱還是數字？

**30 秒版本**
名稱。全域註冊 `JsonStringEnumConverter`，狀態輸出成 `"UnderReview"` 而不是 `1`。數字對前端沒有意義，而且 enum 的順序一改，前端就默默顯示錯誤的狀態；名稱是穩定的合約。

**展開**
- C# 端仍然用 enum 型別（`CaseStatus`），不是 string，編譯器幫忙檢查拼字。
- 測試讀回應時用 `allowIntegerValues: false` 的嚴格設定：如果 API 退回成輸出數字，反序列化直接失敗，而不是悄悄通過。另外有一個測試直接看原始 JSON，不依賴測試端的設定。
- 跟 Java 的對照：Jackson 預設就輸出 enum 的名稱，System.Text.Json 預設是數字，要自己設定。

**證據**：`Program.cs` 的 `AddJsonOptions`、`ApiTestHelpers.JsonOptions`、`CaseWorkflowApiTests.GetCase_Status_IsSerializedAsName`。

---

### 30. 套件有弱點，但它是間接引用的，怎麼處理？

**30 秒版本**
`System.Security.Cryptography.Pkcs` 有兩個阻斷服務的弱點，它是 EF Core → SqlClient 間接帶進來的。升級 EF Core 到最新版也修不到，因為 SqlClient 仍然依賴有問題的版本。我開啟 NuGet 的 transitive pinning，在 `Directory.Packages.props` 直接指定修補後的版本，並寫註解說明原因和什麼時候可以拿掉。

**展開**
- 怎麼發現：`dotnet list package --vulnerable --include-transitive`，只看直接引用的套件會漏掉。
- **集中管理版本**（Central Package Management）：所有專案的套件版本寫在同一個檔案，10.0.x 的套件整組一起升級，不會出現 A 專案 10.0.5、B 專案 10.0.13。
- 指定間接套件的版本有風險（上層套件沒有用這個版本測試過），所以升級後跑完整的測試，並留下「上層更新後拿掉」的註解，避免永遠釘在某個版本。
- 套件修好了還不夠：**執行環境（.NET Runtime）也要更新**到同一個安全性修補版本。
- 政府案常要過弱點掃描，這類「間接相依的弱點」是最常被掃到、也最常被忽略的。

**證據**：`Directory.Packages.props` 的 `Security` 區塊與註解、commit `776c4f4`。

---

### 31. 民眾和承辦人用同一組 API 嗎？

**30 秒版本**
分開。民眾用 `/api/my/cases`，整個 Controller 只有民眾角色能呼叫，範圍一律是「自己送出的案件」；承辦人用 `/api/cases`。兩邊回傳不同的 DTO，民眾看不到承辦端的內部欄位。範圍由端點決定，不是把角色合併，所以承辦人自己也陳情時，兩邊各看各的，不需要「本單位 + 本人」這種聯集的範圍。

**展開**
- **同一個端點依角色回傳不同內容的風險**：之後承辦端加了內部欄位（承辦人備註、內部簽核），只要哪裡忘了判斷角色，就洩漏給民眾。分開之後，民眾的 DTO 根本沒有那些欄位。
- **Policy 加在 Controller 類別上**：之後新增民眾端點不會忘記限制角色；承辦端的 Controller 也不再有任何 `[AllowAnonymous]`。
- **修掉一個已知漏洞**：以前民眾不用登入，補件只能用「全部」的範圍，任何人知道案件 Id 就能補件。現在補件用民眾自己的範圍，補別人的案件得到 404。
- **本人看自己的個資**：不遮罩、不寫查詢紀錄（查詢紀錄是防公務員查看別人的個資），但回應仍然 `no-store`。
- **送件的 `Location` 標頭**：原本指向承辦端的網址，民眾去呼叫會 403；搬過去之後改指向民眾自己的詳情，測試直接用 `Location` 取回案件，證明它真的能用。
- **送件人來自 token 的 `sub`**，不是請求內容，民眾無法冒用別人的身分送件。
- 測試資料：每個測試用不同的民眾 Id，共用的資料庫裡有其他測試的案件，總筆數才能精確比對。

**證據**：`MyCasesController`、`CaseAccessScope.ForPetitioner`、`GetMyCaseHandler`；`MyCasesApiTests`；反向驗證：補件改回「全部」、詳情改用承辦端的範圍、拿掉 Policy 等 6 種 bug 都有測試失敗。

---

### 32. 畫面要顯示另一個聚合的資料（例如單位名稱），怎麼查？

**30 秒版本**
寫入和讀取分開處理。寫入（送件、結案）經過 Repository 和聚合，因為要執行業務規則；讀取只是要顯示，用專用的查詢直接 JOIN、投影成 DTO，一次 SQL 就拿到案件和單位名稱，不載入 Domain 實體。這是簡化版的 CQRS。（設計已定，預計做前端時實作）

**展開**
- **不在 Handler 裡再查一次 `IDepartmentRepository`**：詳情頁只是多一次查詢，但列表 20 筆就變成 N+1；而且 Repository 是用來讀寫聚合的，為了一個名稱載入整個實體，用途不對。
- **不在 `Case` 加導覽屬性 `Department`**：兩個聚合之間只用 Id 參照，加了導覽屬性，`Case` 就能「走到」另一個聚合，界線就不見了（Java JPA 的 `@ManyToOne` 也有同樣的問題）。
- 列表其實已經是這樣做：`Select new CaseSummary(...)` 直接在 SQL 投影。
- 另一個做法：單位很少而且很少變動，前端呼叫一次單位清單自己對照，或後端快取。

---

### 33. 新增必填欄位，資料庫裡的舊資料怎麼辦？

**30 秒版本**
看階段。開發階段沒有真實資料，我直接刪掉資料庫重建，migration 保持乾淨，不寫處理假資料的程式。上線之後就不能這樣做：要分三步，先加可為 null 的欄位、用 SQL 回填舊資料、再改成 NOT NULL，而且回填的值要跟業務單位確認（例如舊案件的送件人要填什麼）。

**展開**
- migration 只描述 schema 的變化，舊資料的處理要另外寫在 migration 的 `Sql()` 裡，或用獨立的資料修正腳本，並且先在正式資料的副本上演練。
- 政府系統的資料不能隨便「補一個預設值」：舊案件沒有送件人時，填「系統匯入」並記錄原因，比假裝有人送件誠實。

**證據**：migration `AddCaseSubmissionAndReplies`（`SubmittedBy`、`SubmittedAt` 直接 NOT NULL）。

---

## 2026-10-20～21：前端（Vue 3 + TypeScript）、登入、民眾端畫面

### 34. 前端怎麼知道登入的是誰？為什麼不直接解開 JWT？

**30 秒版本**
後端提供 `GET /api/me`，回傳「後端實際認定的」身分：Id、姓名、角色、單位，前端用它決定畫面要顯示什麼。前端不解開 Access Token：在 OIDC 裡它是給 API 看的，有些 IdP 的 Access Token 前端根本解不開；而且後端之後會用 `IClaimsTransformation` 轉換角色和單位，token 裡的資料就跟後端認定的不一樣了，前端會出現「畫面顯示有權限，按下去卻 403」。

**展開**
- 前端的判斷只是使用者體驗，真正的權限檢查一定在後端。使用者改了前端的狀態，按下去還是會被後端擋下。
- `/api/me` 沒有業務邏輯，所以不經過 Application 的 Handler：判斷標準是「換成 gRPC 或排程呼叫，這段邏輯還需要嗎？」只跟 HTTP 有關的，就留在 Controller。
- 真實的 IdP 不會給我們資料庫的單位 Guid：`sub`、`name` 是標準欄位；角色沒有標準（Entra ID 的 App Roles、AD FS 的 Claim 規則、Keycloak 的巢狀結構）；單位通常是系統自己的權限表，或 IdP 給代碼、後端在 `IClaimsTransformation` 裡轉換（這就是 `Department.Code` 的用途）。架構上只有 `HttpCurrentUser` 知道 claim 的名稱，換 IdP 時 Handler 和授權都不用改。
- 寫測試時發現：`Claim.ToString()` 回傳的是 `"name: 王小明"`，要用 `.Value`；回應的 `IReadOnlySet<string>` 序列化沒問題，System.Text.Json 卻讀不回來，API 合約改用 JSON 能表達的 `IReadOnlyList`。兩個都是測試把回應真的讀回來才抓到。

**證據**：`MeController`、`MeResponse`；`MeAndDepartmentsApiTests`。

---

### 35. 前後端分開開發，怎麼連接？需要 CORS 嗎？

**30 秒版本**
不需要。開發時由 Vite 的 proxy 把 `/api`、`/dev` 轉給 ASP.NET Core，瀏覽器只看到同一個來源。正式環境也是同一個網域：IIS 用 URL Rewrite 轉發，或直接把建置出來的靜態檔案放進 ASP.NET Core 的 `wwwroot`，所有不認識的路徑用 `MapFallbackToFile("index.html")` 交給 Vue Router。開發和正式的架構一致，就不需要 CORS。

**展開**
- 跟 Next.js 的 `rewrites` 不同：Next 在正式環境有 Node.js 伺服器，rewrites 一直有效；Vite 的 proxy 只在 `npm run dev` 時存在，建置出來只是靜態檔案。這也是不用 Nuxt 的原因之一：系統全部要登入、不需要 SEO，多一個 Node.js 服務只是多一項維護和弱點掃描。
- 踩過的坑：proxy 一開始指向 http 的 port，後端用 https 設定檔啟動時，`UseHttpsRedirection` 回 307 把瀏覽器轉到 https 的 port，就變成跨來源。用 curl 看到 307 和 `Location` 才找到原因，改指向 https，並加上 `secure: false`（開發憑證是自己簽的）。
- 前後端放在同一個 repo：後端改欄位時，前端的型別定義在同一個 commit 一起改。

**證據**：`CaseTrack.Web/vite.config.ts`。

---

### 36. token 存在哪裡？有什麼資安考量？

**30 秒版本**
開發階段存在 `sessionStorage`：重新整理後還在、關掉分頁就清掉，每個分頁各自獨立，可以一個分頁當民眾、另一個當承辦人。缺點是 XSS 讀得到。正式環境我會讓後端兼任 BFF：用 `AddOpenIdConnect` 對接 IdP，token 留在伺服器，瀏覽器只拿 HttpOnly、SameSite 的 cookie，再加上 antiforgery token 防 CSRF。

**展開**
- 程式裡 token 有兩份：記憶體的 `ref`（響應式，執行時用的）和 `sessionStorage`（存檔，載入時讀一次）。所有修改都經過同一個 `setToken()`，兩份不會不同步。
- 兩種攻擊的取捨：Bearer token 在標頭裡，瀏覽器不會自動帶上，所以沒有 CSRF，但 XSS 偷得走；cookie 是 HttpOnly 的，XSS 偷不走，但瀏覽器會自動帶上，要靠 SameSite 和 antiforgery（就是 CSRF token）防範。
- cookie 只能防止 token 被偷走，XSS 還是能在頁面開著時冒用身分，所以 XSS 本身也要防：Vue 的 `{{ }}` 會跳脫，不用 `v-html` 顯示使用者輸入的內容。
- 不一定需要獨立的 BFF 服務：API 只服務自己的前端時，API 本身就能發 cookie；API 同時服務手機 App、其他系統時，才需要獨立的 BFF 把 cookie 換成 token。

---

### 37. 前端的登入狀態和導頁怎麼設計？遇過什麼問題？

**30 秒版本**
登入狀態放在一個 composable 的「檔案層級」變數裡，整個 app 共用一份。換頁時由 router 的 `beforeEach` 檢查：沒登入就導到登入頁並帶上 `redirect`；重新整理後由它呼叫 `/api/me` 取回身分，token 過期就在顯示頁面之前導走。停在同一頁、狀態改變時（按登出、操作途中 token 過期），由 `App.vue` 監看登入狀態導頁。`apiFetch` 收到 401 只負責登出。

**展開**
- 一開始讓 `apiFetch` 收到 401 就導頁，結果在 `beforeEach` 裡呼叫 `router.push` 會開始另一次換頁，原本的被取消，`redirect` 帶的是「換頁之前」的頁面。改成職責分開：呼叫 API 的不負責導頁，誰知道使用者要去哪，誰就負責導頁。
- `useRouter()` 底層是 `inject()`，只能在元件的 setup 期間呼叫；在 `apiFetch` 裡呼叫只會拿到 `undefined`。我寫了一段程式實際確認，改成直接 import router 的實例。
- `isLoggedIn` 看 token 不看 `me`：token 從 `sessionStorage` 同步讀出，`me` 要等 API。用 `me` 判斷的話，重新整理後在 API 回應之前都是「沒登入」。
- `!isLoggedIn` 少了 `.value` 永遠是 `false`，沒登入也會放行。TypeScript 和 ESLint 都沒抓到（ESLint 的規則認不出從函式解構出來的 ref），只有實際操作才發現。
- 登入後的 `redirect` 只接受站內路徑（`/` 開頭、不是 `//`），防止 Open Redirect：攻擊者用真的政府網域加上 `?redirect=假網站` 釣魚。

**證據**：`useAuth.ts`、`router/index.ts`、`App.vue`、`LoginView.vue`；瀏覽器驗證：無效的 token 打開 `/my/cases/new`，被導到 `/login?redirect=/my/cases/new`。

---

### 38. 前端怎麼處理 API 的錯誤？

**30 秒版本**
所有 API 呼叫經過一個 `apiFetch`：失敗一律拋出 `ApiError`，保留狀態碼和 ProblemDetails。表單收到 400 時，用 `errors` 的 key（C# 的屬性路徑，例如 `Petitioner.Name`）把錯誤顯示在對應的欄位旁邊；Domain 的錯誤沒有 `errors`，顯示 `detail`；框架預設的英文 title 不顯示給民眾。

**展開**
- 用 `===` 比對 `Content-Type` 永遠不成立：Controller 回的是 `application/problem+json; charset=utf-8`。啟動後端實際確認後改用 `startsWith`。
- 送 JSON 沒設 `Content-Type`，`fetch` 預設 `text/plain`，ASP.NET Core 回 415。
- 204 沒有內容，`response.json()` 會拋例外，所以直接回傳；呼叫端寫 `apiFetch<void>`。
- `apiFetch<T>` 的 T 只是呼叫端的承諾：TypeScript 的型別在執行時被擦掉了。要保證型別有兩個不同的問題：型別定義跟後端不一致，用 OpenAPI（Swagger）自動產生型別；執行時收到的資料不對，用 Zod。後端是自己寫的、API 測試已經用嚴格的設定讀回應，前者比較重要。
- 網址少了開頭的 `/`，在 `/my/cases/123` 頁面上會變成 `/my/cases/api/...`，不經過 proxy，Vite 回傳 `index.html`（200），錯誤訊息是 `Unexpected token '<'`，完全看不出原因。

**證據**：`api/client.ts`、`api/myCases.ts`、`SubmitCaseView.vue`。

---

### 39. 表單怎麼防止重複送出？

**30 秒版本**
兩層：送出函式第一行檢查「送出中」就直接返回，按鈕再用 `disabled` 讓使用者看得出不能按。只停用按鈕不夠，Vue 要等下一輪才更新畫面，在那之前的點擊還是會觸發。而且按鈕只能綁表單的 `submit`，不能再綁 `click`。

**展開**
- 實際重現過：`type="submit"` 的按鈕同時綁了 `@click`，點擊會觸發按鈕的 click 和表單的 submit，一次點擊就建立了兩件案件。修好後連點兩下也只有一件。
- 前端的防護擋不住重新整理後再送、網路逾時重試這些情況。真正的保證要在後端：請求帶一個冪等性的鍵（Idempotency-Key），後端記錄已處理過的鍵。目前沒做，是之後可以加強的地方。

**證據**：`SubmitCaseView.vue`；反向驗證：放回原本的寫法，點一次建立兩件。

---

### 40. 列表的分頁狀態放在哪裡？

**30 秒版本**
放在網址上（`?page=2`），而且網址是唯一的來源：頁碼用 `computed` 從網址算出來，換頁只改網址，再由 `watch` 重新載入。這樣重新整理、瀏覽器的上一頁、分享網址都會停在同一頁。

**展開**
- 一開始另外存了一份 `ref`，按瀏覽器的上一頁時，網址變了，列表卻沒變：只換查詢字串時 Vue Router 會重複使用元件，`onMounted` 不會再執行，`ref` 一直是舊的頁碼。這跟 token 存兩份是同一個教訓：同一份資料只存一個地方，其他的用算的。
- 網址是使用者能改的輸入：`?page=abc`、`0`、負數、重複的參數都改用預設值，不然送出 `?page=NaN`，後端回 400；超過最後一頁時用 `router.replace` 換到最後一頁，不在瀏覽紀錄留下不存在的頁碼。

**證據**：`MyCaseListView.vue`、`utils/query.ts`；瀏覽器驗證：下一頁 → 上一頁回到第 1 頁、`?page=99` 換到最後一頁。

---

### 41. 前端畫面還注意了哪些資安和無障礙的細節？

**30 秒版本**
使用者輸入的內容一律用 `{{ }}` 輸出，換行用 CSS 的 `white-space: pre-wrap` 保留，不把換行換成 `<br>` 再用 `v-html`，那樣會讓民眾輸入的 HTML 在承辦人的畫面上被執行。我實際送了一段 `<img onerror>`，畫面上顯示成文字，沒有被執行。表單每個欄位都有 `label`，錯誤用 `aria-invalid`、`aria-describedby` 跟欄位連起來，政府網站的無障礙檢測會看這些。

**展開**
- 單筆資料的詳情用 `<dl>` 不用表格：螢幕報讀會念成「主旨：…」，也不會有欄位對不齊的問題。
- 狀態的中文名稱用 `Record<CaseStatus, string>`：後端新增狀態、前端型別跟著加上之後，這裡沒補就編譯失敗。
- 日期用 `Intl.DateTimeFormat` 指定 `Asia/Taipei`：不指定就用使用者電腦的時區，UTC 的晚上在台灣已經是隔天。而且每個欄位都要列出來，只寫 `hour12` 的話預設只顯示日期。
- 補件後重新載入，不在前端自己改狀態：狀態轉換是後端 Domain 的規則，前端改等於再寫一次。
- 一個只有實際操作才發現的 bug：409 的提示放在補件表單裡，重新載入後狀態不是「補件中」，整個表單被隱藏，提示也跟著消失。改放到表單外面。

**證據**：`MyCaseDetailView.vue`、`SubmitCaseView.vue`、`utils/caseStatus.ts`、`utils/format.ts`。

---

## 2026-10-22：允許的動作（allowedActions）、承辦端畫面、完整流程

### 42. 前端怎麼知道使用者現在能做哪些動作？

**30 秒版本**
後端在案件詳情裡直接回傳 `allowedActions`，例如 `["RequestSupplement", "Close", "Reject", "ViewPetitioner"]`，前端只判斷「清單裡有沒有」，完全不知道角色和狀態的規則。這是輕量版的 HATEOAS。能不能做由兩件事決定：角色和案件的狀態，兩條規則在後端各自只寫一次，`allowedActions` 和真正執行動作時讀的是同一份。

**展開**
- 前端自己判斷（`status === 'UnderReview'` 就顯示結案）等於在前端再寫一次 Domain 的規則，後端改了規則、前端忘了改，按鈕就顯示錯了。
- 只把判斷搬到後端也不夠：如果在 Handler 裡另外寫 `if (status == UnderReview) actions.Add(Close)`，還是重複的規則。所以狀態規則抽成 Domain 的 `CanClose` 等屬性，`Close()` 本身也改成讀它；角色規則抽成 `Roles` 的分組，`Program.cs` 的 Policy 也改用它。`CaseActions.AllowedFor` 只負責把兩者組合起來，自己沒有任何規則。
- 一個人有多個角色（分派人員兼承辦人）時，兩組動作合併；管理者、民眾、不認識的角色一律是空的（預設拒絕）。
- 不用檢查「看不看得到這件案件」：查詢時已經用資料範圍過濾，看不到的案件直接 404，走不到這裡。
- 後端的檢查都還在：畫面上的清單過時了（例如別人剛處理過），按下去會收到 409，前端提示並重新載入。
- 還有一個缺口：Controller 上的 `[Authorize(Policy)]` 被改錯時，`allowedActions` 不會發現（它讀的是角色分組，不是 Policy）。目前靠原本的授權測試抓到，之後可以加跨層的一致性測試：對每個動作實際呼叫端點，比對結果和 `allowedActions` 是否一致。

**證據**：`Case.CanXxx`、`CaseActions.AllowedFor`、`Roles`；`CaseActionsTests`、`AllowedActionsApiTests`。

---

### 43. 怎麼保證「能不能做」的判斷跟動作本身不會不同步？

**30 秒版本**
寫一組一致性測試：6 種案件情境（已送出且未分派、已送出且已分派、審核中、補件中、已結案、已退件）乘上 7 個動作，共 42 個組合，每一個都檢查「`CanXxx` 是 true ⟺ 動作真的會成功」。另外一個測試檢查情境涵蓋了所有狀態，以後新增狀態忘了補情境就會失敗。

**展開**
- 情境存的是「建立案件的函式」而不是案件本身：動作會改變案件，每個測試都要拿到全新的案件。案件的狀態是 `private set`，所以每個情境都照真實的流程推過去，測試裡的案件一定是合法的狀態。
- 判斷動作有沒有成功時，只接住 Domain 拒絕動作會丟的三種例外（C# 的 `catch ... when` 例外篩選器）。用 `catch (Exception)` 全部接住的話，程式錯誤（例如 `NullReferenceException`）會被當成「不允許」，測試可能因為錯誤的原因通過。
- 反向驗證時有一個「改了卻沒失敗」的情況：把 `CanAssign` 改成審核中也能分派，`AssignTo()` 也讀 `CanAssign`，兩邊一起變了，一致性測試當然通過。這正是規則只寫一次的效果；規則本身改錯了，是由原本的狀態轉換測試抓到。兩組測試分工：狀態轉換測試管「規則對不對」，一致性測試管「判斷和動作有沒有不同步」。
- 反向驗證本身也要驗證：有一次腳本把連線字串的反斜線吃掉，API 測試全部 0ms 失敗，看起來像「抓到了」，其實是測試根本沒跑起來。0ms 的失敗代表 fixture 起不來，不能當成反向驗證成功。

**證據**：`CaseCapabilityConsistencyTests`、`CaseStatusTransitionTests`。

---

### 44. 角色和權限的對應寫在哪裡？

**30 秒版本**
寫在 Application 的 `Roles` 裡，用分組表示：`CaseHandlers`（承辦人、主管）、`CaseDispatchers`（分派人員）、`CaseReaders` 由前兩組合併而來。`Program.cs` 的 Policy、資料範圍 `CaseAccessScope`、`allowedActions` 都用這些分組，新增角色（例如副主管）只要改一個地方。

**展開**
- 分組用 `FrozenSet`：`HashSet` 宣告成 `IReadOnlySet` 還是能被轉型回去修改，這些集合決定整個系統的授權，而且全域共用，必須真的不能改。
- `CaseReaders` 寫成兩組的聯集，不自己再列一次三個角色：新增角色到 `CaseHandlers`，查詢權限自動跟著有。但靜態欄位依宣告順序初始化（Java 也一樣），它必須寫在另外兩組後面，否則會拿到 null，整個類別初始化失敗。
- 角色用字串常數不用 enum：值來自 IdP，會出現系統不認識的角色（例如其他系統的角色），用字串時不認識的角色自然比對不到任何 Policy，就是預設拒絕；框架的 `RequireRole`、`IsInRole` 也只吃字串。

**證據**：`Roles`、`Program.cs` 的 `AddAuthorizationBuilder`、`CaseAccessScope.For`。

---

### 45. Vue 的元件怎麼設計？子元件怎麼把結果告訴父元件？

**30 秒版本**
要求補件、結案、退件、不予受理四個動作的流程完全一樣（按鈕 → 填寫 → 送出），抽成一個元件 `ActionWithReason`，真正呼叫哪個 API 由父元件傳一個函式 prop 進來。不用 emit，是因為元件要 `await` 結果才知道該收起來還是顯示錯誤，emit 是單向的，拿不到回傳值。後來加「不予受理」只多了 7 行。

**展開**
- emit 適合「不需要回覆」的通知（對照 Angular 的 `@Output`）；需要結果時傳函式（對照 React 的 callback props）。用 emit 的話，父元件還得另外傳 `submitting`、`error` 回來，狀態會散在兩個地方。
- 同一頁會有好幾個這個元件，`id` 寫死會重複，`label` 只會對到第一個輸入框（螢幕報讀也會念錯），用 Vue 3.5 的 `useId()` 產生唯一的 id。
- 結果訊息放在元件外面：處理完重新載入後，`allowedActions` 變了，元件可能整個消失，放在裡面的訊息會跟著不見（前一天的補件表單也踩過一樣的坑）。
- `<style scoped>` 只會套到子元件的根元素，抽出 `SupplementHistory` 時裡面用到的樣式要一起搬，否則畫面會走樣。
- 不是每個東西都要抽：分派的下拉選單只有一個地方用到，直接寫在頁面裡，多一層元件沒有好處。
- 共用的資料用 composable（`useDepartments`），狀態寫在函式外面、全 app 只載入一次。不能在模組最外層 `await`：那樣「什麼時候呼叫 API」就變成「什麼時候被 import」，登入前被 import 會 401，失敗時連帶 import 它的頁面也載入失敗，而且永遠不會重試。

**證據**：`ActionWithReason.vue`、`SupplementHistory.vue`、`useDepartments.ts`、`CaseDetailView.vue`。

---

### 46. 承辦人查看完整個資，前端做了哪些保護？遇過什麼問題？

**30 秒版本**
詳情一律顯示遮罩後的資料，承辦人按「查看完整資料」才呼叫 API（後端每次都寫一筆查詢紀錄，回應是 `no-store`）。完整資料只存在這個頁面的狀態，不放 sessionStorage 或全 app 共用的狀態；有「隱藏」按鈕，換案件或重新載入時自動回到遮罩；顯示時整張卡片換成醒目的顏色，按鈕旁提示「查看會留下紀錄」。

**展開**
- 抓到一個競態問題：在 A 案件按下查看，回應還沒回來就換到 B 案件，A 的完整個資會顯示在 B 的頁面上，等於個資外洩。改成送出前記下是哪一件，回應回來時確認還是同一件才採用。我把回應延遲兩秒重現了這個問題，拿掉檢查時確實會顯示錯的資料，加上檢查後不會。一般的案件詳情也有同樣的問題，一起修了。
- 連點兩下會寫兩筆查詢紀錄，紀錄就不準了，所以送出中要擋住重複點擊。
- 提示「查看會留下紀錄」是告知，也是一種嚇阻；查看權限只看角色不看狀態（結案後民眾來電，承辦人也要聯絡得到人），分派人員看不到這個按鈕。
- 實務上很多政府系統還會要求填寫查詢事由，一起寫進紀錄，這是之後可以加強的地方。

**證據**：`CaseDetailView.vue`；瀏覽器驗證：延遲回應後換案件，新案件維持遮罩；反向驗證：拿掉檢查就會顯示前一件的完整資料。

---

### 47. 你怎麼確認整個系統真的能用？

**30 秒版本**
除了 339 個後端測試，我在瀏覽器開兩個分頁，完全透過畫面跑完一件案件：民眾送件 → 分派人員篩選待分派並分派 → 承辦人受理、要求補件 → 民眾補件 → 承辦人查看完整個資、結案 → 民眾看到結案回覆，最後換成另一位民眾直接打開那件案件的網址，確認看不到。sessionStorage 每個分頁各自獨立，剛好可以同時登入不同身分。

**展開**
- 前端的錯誤很多是工具抓不到的：少了 import 的元件（只會變成不認識的標籤）、`select` 事件綁在下拉選單上（永遠不會觸發）、一次點擊觸發兩次送出、訊息跟著元件一起消失。這些都是實際在瀏覽器操作才發現的。
- 驗證用的資料庫是自己的 LocalDB 執行個體，不碰開發者日常使用的資料庫；伺服器用不同的 port 啟動，驗證完就關掉。

**證據**：10/22 的瀏覽器驗證紀錄；`CaseTrack.Web` 的各個頁面。
