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

**證據**：`CasesController`、`HttpCurrentUser`、`HttpCurrentUserTests`；資料列層級過濾正在實作（`CaseAccessScope`）。

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
