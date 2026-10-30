using CaseTrack.Application.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.Infrastructure.Persistence;

// 每天一列計數器：UPDATE ... OUTPUT 在同一個陳述式裡完成「讀出、加 1、寫回」，併發時不會重複。
// 取號和存案件是分開的交易，存檔失敗時會跳號（業務上允許）。
// 用哪一天、編號的格式都不在這裡決定（Domain 的 CaseNumber），這裡只保證同一天的數字不重複
internal sealed class SqlCaseNumberSequence : ICaseNumberSequence
{
    private readonly CaseTrackDbContext _dbContext;

    public SqlCaseNumberSequence(CaseTrackDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> NextAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            // 用 ToListAsync，不能用 SingleAsync／FirstAsync：那些會讓 EF 把這段 SQL 包成子查詢再加上筆數限制，
            // 例如 SingleAsync 會變成 SELECT TOP(2) [s].[Value] FROM (UPDATE ... OUTPUT ...) AS [s]
            // （TOP(2) 是為了判斷「剛好一筆」：查到第 2 筆就要拋例外，所以最多只需要讀 2 筆）。
            // UPDATE 不能當子查詢，SQL Server 會回語法錯誤；ToListAsync 不包裝，原樣執行。
            // {date} 會變成參數 @p0（參數型別是 FormattableString），不是字串串接，沒有 SQL injection 的問題
            var updated = await _dbContext.Database
                .SqlQuery<int>($"UPDATE CaseNumberCounters SET LastValue = LastValue + 1 OUTPUT INSERTED.LastValue AS Value WHERE [Date] = {date}")
                .ToListAsync(cancellationToken);

            if (updated.Count == 1)
            {
                return updated[0];
            }

            // 今天還沒有計數器：建立它，這個請求拿到 1 號
            try
            {
                await _dbContext.Database.ExecuteSqlAsync(
                    $"INSERT INTO CaseNumberCounters ([Date], LastValue) VALUES ({date}, 1)",
                    cancellationToken);
                return 1;
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                // 別的請求搶先建立了今天的計數器，回到迴圈重新 UPDATE
            }
        }
    }
}
