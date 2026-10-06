# EF Core Migrations 指令

所有指令都在 **solution 根目錄**（`CaseTrack.slnx` 所在位置）執行。

## 前置需求

```bash
# 安裝 EF Core 命令列工具（每台電腦裝一次）
dotnet tool install --global dotnet-ef

# 之後升級
dotnet tool update --global dotnet-ef
```

- 開發環境資料庫：SQL Server LocalDB（執行個體 `MSSQLLocalDB`）
- 連線字串：`CaseTrack.Api/appsettings.Development.json` 的 `ConnectionStrings:CaseTrack`

## 為什麼每個指令都要帶兩個專案參數

| 參數 | 值 | 原因 |
|---|---|---|
| `--project` | `CaseTrack.Infrastructure` | DbContext 和 Migration 檔放在這裡 |
| `--startup-project` | `CaseTrack.Api` | `dotnet ef` 會真的啟動 Api 的 `Program.cs`，從 DI 取得 DbContext；連線字串也在 Api 的設定檔裡 |

所以 Api 必須安裝 `Microsoft.EntityFrameworkCore.Design`，DbContext 的建構子也必須是 `public`（DI 用反射建立物件）。

## 日常流程：修改模型後

```bash
# 1. 產生 migration（名稱用動詞開頭，描述這次改了什麼，例如 AddPetitionerFields）
dotnet ef migrations add <名稱> --project CaseTrack.Infrastructure --startup-project CaseTrack.Api --output-dir Persistence/Migrations

# 2. 審查產生的 Up()／Down()，以及要執行的 SQL
dotnet ef migrations script --project CaseTrack.Infrastructure --startup-project CaseTrack.Api --idempotent --output migration.sql

# 3. 套用到本機 LocalDB
dotnet ef database update --project CaseTrack.Infrastructure --startup-project CaseTrack.Api
```

`migration.sql` 只是用來審查的，看完就刪掉，不要 commit（隨時可以重新產生）。

## 查詢

```bash
# 列出所有 migration，以及是否已套用到資料庫（未套用的會標示 Pending）
dotnet ef migrations list --project CaseTrack.Infrastructure --startup-project CaseTrack.Api

# 確認 DbContext 能從 DI 建立、讀到哪個資料庫（不會真的連線）
dotnet ef dbcontext info --project CaseTrack.Infrastructure --startup-project CaseTrack.Api

# 依照「目前的模型」產生完整的建表 SQL（不看 migration，用來快速檢查對應設定）
dotnet ef dbcontext script --project CaseTrack.Infrastructure --startup-project CaseTrack.Api
```

## 回復與重來

```bash
# 刪掉最後一個 migration（只能用在「還沒套用到任何共用環境」的 migration）
# 如果已經套用到本機 DB，要先用下一個指令退回去
dotnet ef migrations remove --project CaseTrack.Infrastructure --startup-project CaseTrack.Api

# 把資料庫退回到指定的 migration（會執行之後每個 migration 的 Down()）
dotnet ef database update <要退回到的 migration 名稱> --project CaseTrack.Infrastructure --startup-project CaseTrack.Api

# 退回到完全空白（執行所有 Down()）
dotnet ef database update 0 --project CaseTrack.Infrastructure --startup-project CaseTrack.Api

# 刪除整個本機資料庫（會詢問確認；加 --force 跳過詢問）
dotnet ef database drop --project CaseTrack.Infrastructure --startup-project CaseTrack.Api
```

## 部署到正式環境

正式環境**不使用** `database update`，也不在程式啟動時呼叫 `Database.Migrate()`：

```bash
# 產生可以重複執行的 SQL 腳本，交給 DBA 審查後執行
dotnet ef migrations script --project CaseTrack.Infrastructure --startup-project CaseTrack.Api --idempotent --output deploy.sql

# 只產生某個區間的變更（從 <起> 之後到 <迄>）
dotnet ef migrations script <起> <迄> --project CaseTrack.Infrastructure --startup-project CaseTrack.Api --idempotent --output deploy.sql
```

原因：
- 程式使用的資料庫帳號只給讀寫資料的權限，**不給修改 schema 的權限**（權限分離，資安稽核要求）
- schema 變更要經過 DBA 審查與變更管理流程
- 多台伺服器同時啟動時，自動 migrate 會互相搶著執行

`--idempotent` 會讓腳本先查 `__EFMigrationsHistory`，已經執行過的版本會跳過，所以重複執行也不會出錯。

## 規則

1. **Migration 檔（含 `CaseTrackDbContextModelSnapshot.cs`）一定要 commit。**
   EF 是拿「目前模型」和 Snapshot 比較來算出差異，不是比較資料庫。
2. **已經套用到任何共用環境的 migration 不能修改**，要改就新增一個 migration。
3. 多人同時新增 migration 時，Snapshot 容易衝突。合併後要重新產生衝突的那個 migration。

## 查看資料庫

- Visual Studio：**View → SQL Server Object Explorer** → `(localdb)\MSSQLLocalDB` → Databases → `CaseTrack`
- 命令列：

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -d CaseTrack -E -Q "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory"
```
