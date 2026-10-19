using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Common;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

// 「沒有紀錄就不准看」：查詢紀錄存檔失敗時，Handler 不能回傳任何個資。
// 用假的依賴才能製造「存檔失敗」，整合測試很難做到
public class GetPetitionerHandlerTests
{
    private static readonly Guid DepartmentId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_SavingAccessRecordFails_ThrowsWithoutReturningData()
    {
        var @case = CreateAssignedCase();
        var accessLog = new RecordingAccessLog();
        var handler = CreateHandler(@case, accessLog, new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(@case.Id, TestContext.Current.CancellationToken));
        Assert.Single(accessLog.Records);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_RecordsCurrentUserAndSaves()
    {
        var @case = CreateAssignedCase();
        var accessLog = new RecordingAccessLog();
        var unitOfWork = new CountingUnitOfWork();
        var handler = CreateHandler(@case, accessLog, unitOfWork);

        var petitioner = await handler.HandleAsync(@case.Id, TestContext.Current.CancellationToken);

        Assert.Equal("0912345678", petitioner.Phone);
        var record = Assert.Single(accessLog.Records);
        Assert.Equal(@case.Id, record.CaseId);
        Assert.Equal("u-officer-1", record.AccessedBy);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    private static GetPetitionerHandler CreateHandler(Case @case, IPersonalDataAccessLog accessLog, IUnitOfWork unitOfWork) =>
        new(new SingleCaseRepository(@case),
            new FixedCurrentUser("u-officer-1", new HashSet<string> { Roles.CaseOfficer }, DepartmentId),
            accessLog,
            unitOfWork,
            TimeProvider.System);

    private static Case CreateAssignedCase()
    {
        var @case = new Case("20261016-0001", "路燈不亮", "中山路路燈故障",
            new Petitioner("王小明", "0912345678", null, null),
            "u-citizen-1", DateTimeOffset.UtcNow);
        @case.AssignTo(DepartmentId);
        return @case;
    }

    // ── 假的依賴 ─────────────────────────────────────────

    private sealed class SingleCaseRepository(Case @case) : ICaseRepository
    {
        public Task<Case?> GetByIdAsync(Guid id, CaseAccessScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<Case?>(id == @case.Id ? @case : null);

        public void Add(Case newCase) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetOverdueSupplementCaseIdsAsync(
            DateTimeOffset now, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PagedResult<CaseSummary>> ListAsync(
            CaseListQuery query, CaseAccessScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAccessLog : IPersonalDataAccessLog
    {
        public List<(Guid CaseId, string AccessedBy)> Records { get; } = [];

        public void Record(Guid caseId, string accessedBy, DateTimeOffset accessedAt) =>
            Records.Add((caseId, accessedBy));
    }

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("資料庫暫時無法使用");
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
