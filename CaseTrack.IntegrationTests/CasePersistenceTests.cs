using CaseTrack.Domain.Cases;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.IntegrationTests;

public class CasePersistenceTests : IClassFixture<DatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(8));
    private readonly DatabaseFixture _fixture;

    public CasePersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveAndReload_WithSupplementRequests_RestoresAllFields()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障");
        @case.StartReview();
        @case.RequestSupplement("缺照片", Now);
        @case.SubmitSupplement(Now.AddDays(1));
        @case.RequestSupplement("缺地址", Now.AddDays(2));

        // Act：用一個 DbContext 寫入
        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Cases.Add(@case);
            await writeContext.SaveChangesAsync(cancellationToken);
        }

        // Act：用另一個全新的 DbContext 讀出
        await using var readContext = _fixture.CreateContext();
        var loaded = await readContext.Cases
            .Include(c => c.SupplementRequests)
            .SingleAsync(c => c.Id == @case.Id, cancellationToken);

        // Assert
        Assert.Equal(CaseStatus.AwaitingSupplement, loaded.Status);
        Assert.Equal("路燈不亮", loaded.Subject);

        var requests = loaded.SupplementRequests.OrderBy(r => r.Sequence).ToList();
        Assert.Equal(2, requests.Count);
        Assert.Equal("缺照片", requests[0].Reason);
        Assert.Equal(Now.AddDays(1), requests[0].SubmittedAt);
        Assert.Equal(TimeSpan.FromHours(8), requests[0].RequestedAt.Offset);
        Assert.Null(requests[1].SubmittedAt);
    }

    [Fact]
    public async Task RequestSupplement_OnReloadedCase_InsertsNewRequest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(NewCaseNumber(), "路燈不亮", "中山路路燈故障");
        @case.StartReview();

        // Arrange：用一個 DbContext 寫入
        await using (var writeContext = _fixture.CreateContext())
        {
            writeContext.Cases.Add(@case);
            await writeContext.SaveChangesAsync(cancellationToken);
        }

        // Act：用另一個全新的 DbContext 讀出，並加入一個補件請求
        await using (var firstReadContext = _fixture.CreateContext())
        {
            var firstLoaded = await firstReadContext.Cases
                .Include(c => c.SupplementRequests)
                .SingleAsync(c => c.Id == @case.Id, cancellationToken);
            firstLoaded.RequestSupplement("缺照片", Now);
            await firstReadContext.SaveChangesAsync(cancellationToken);
        }

        // Act：再用另一個全新的 DbContext 讀出
        await using var secondReadContext = _fixture.CreateContext();
        var secondLoaded = await secondReadContext.Cases
            .Include(c => c.SupplementRequests)
            .SingleAsync(c => c.Id == @case.Id, cancellationToken);

        // Assert
        Assert.Equal(CaseStatus.AwaitingSupplement, secondLoaded.Status);
        Assert.Equal("路燈不亮", secondLoaded.Subject);

        var requests = secondLoaded.SupplementRequests;
        var request = Assert.Single(requests);
        Assert.Equal("缺照片", request.Reason);
        Assert.Equal(TimeSpan.FromHours(8), request.RequestedAt.Offset);
        Assert.Null(request.SubmittedAt);
    }

    [Fact]
    public async Task SaveChanges_WithDuplicateCaseNumber_ThrowsUniqueViolation()
    {
        // Arrange：第一個案件先成功存進去（模擬先到的請求）
        var cancellationToken = TestContext.Current.CancellationToken;
        var caseNumber = NewCaseNumber();
        var case1 = new Case(caseNumber, "路燈不亮", "中山路路燈故障");
        var case2 = new Case(caseNumber, "水管破裂", "中山路水管破裂");

        await using (var firstContext = _fixture.CreateContext())
        {
            firstContext.Cases.Add(case1);
            await firstContext.SaveChangesAsync(cancellationToken);
        }

        // Act & Assert：後到的請求用同一個編號，被資料庫的唯一索引擋下
        await using (var secondContext = _fixture.CreateContext())
        {
            secondContext.Cases.Add(case2);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => secondContext.SaveChangesAsync(cancellationToken));

            // 確認是「因為編號重複」而失敗，而不是其他寫入錯誤（2601 = 唯一索引鍵值重複）
            var sqlException = Assert.IsType<SqlException>(exception.InnerException);
            Assert.Equal(2601, sqlException.Number);
        }

        // Assert：先到的案件不受影響，資料庫裡這個編號只有一筆
        await using var readContext = _fixture.CreateContext();
        var saved = await readContext.Cases
            .Where(c => c.CaseNumber == caseNumber)
            .ToListAsync(cancellationToken);
        Assert.Equal(case1.Id, Assert.Single(saved).Id);
    }

    private static string NewCaseNumber() => $"T-{Guid.NewGuid():N}"[..20];
}
