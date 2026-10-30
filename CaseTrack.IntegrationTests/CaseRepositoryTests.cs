using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Domain.Cases;
using CaseTrack.Infrastructure;
using CaseTrack.Infrastructure.Persistence.Configurations;
using Microsoft.Extensions.DependencyInjection;

namespace CaseTrack.IntegrationTests;

// 透過 AddInfrastructure 的 DI 取得 Repository，測的是正式環境實際使用的組合（包括 DI 註冊方式）
[Collection(nameof(DatabaseCollection))]
public class CaseRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task GetByIdAsync_WithSupplementRequests_IncludesRequests()
    {
        // Arrange：在一個 scope（模擬一個請求）裡新增並存檔
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(TestData.NewCaseNumber(), "路燈不亮", "瑞光路301號附近", TestData.Petitioner, TestData.SubmittedBy, TestData.SubmittedAt, []);
        @case.AssignTo(TestData.DepartmentId);
        @case.StartReview();
        @case.RequestSupplement("缺照片", Now);

        await using (var writeScope = CreateScope())
        {
            writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Act：在另一個 scope（另一個請求）裡讀出
        await using var readScope = CreateScope();
        var reloaded = await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetByIdAsync(@case.Id, CaseAccessScope.All, cancellationToken);

        // Assert
        Assert.NotNull(reloaded);
        Assert.Equal(CaseStatus.AwaitingSupplement, reloaded.Status);
        Assert.Equal("路燈不亮", reloaded.Subject);
        var request = Assert.Single(reloaded.SupplementRequests);
        Assert.Equal("缺照片", request.Reason);
        Assert.Equal(TimeSpan.FromHours(8), request.RequestedAt.Offset);
        Assert.Null(request.SubmittedAt);
    }

    // 兩件事：① GetByIdAsync 有 Include 附件（漏了不會有任何錯誤，只是附件永遠是空的）
    // ② 在讀出來的案件上補件並加附件，存得進去：附件的 Id 由 Domain 產生，沒有 ValueGeneratedNever 的話，
    //    EF 看到已經有 Id，會當成既有的資料送出 UPDATE（更新 0 筆 → 併發例外），而不是 INSERT
    [Fact]
    public async Task SubmitSupplement_OnReloadedCase_SavesAttachmentsAndGetByIdIncludesThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var @case = new Case(TestData.NewCaseNumber(), "路面坑洞", "中山路有坑洞", TestData.Petitioner, TestData.SubmittedBy, TestData.SubmittedAt,
            [new NewAttachment("現場.jpg", "image/jpeg", 2048)]);
        @case.AssignTo(TestData.DepartmentId);
        @case.StartReview();
        @case.RequestSupplement("請補地圖", Now);

        await using (var writeScope = CreateScope())
        {
            writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Act：另一個請求讀出案件、補件並附上檔案
        Guid supplementAttachmentId;
        await using (var actScope = CreateScope())
        {
            var loaded = await actScope.ServiceProvider.GetRequiredService<ICaseRepository>()
                .GetByIdAsync(@case.Id, CaseAccessScope.All, cancellationToken);
            Assert.NotNull(loaded);
            supplementAttachmentId = loaded.SubmitSupplement("補上地圖", Now.AddDays(1),
                [new NewAttachment("地圖.pdf", "application/pdf", 4096)]).Single().Id;
            await actScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }

        // Assert：再用另一個請求讀出，兩個附件都在，欄位都還原
        await using var readScope = CreateScope();
        var reloaded = await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetByIdAsync(@case.Id, CaseAccessScope.All, cancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded.Attachments.Count);

        var original = reloaded.Attachments.Single(a => a.SupplementRequestId is null);
        Assert.Equal(@case.Attachments.Single().Id, original.Id);
        Assert.Equal("現場.jpg", original.FileName);
        Assert.Equal("image/jpeg", original.ContentType);
        Assert.Equal(2048, original.Size);
        Assert.Equal(TestData.SubmittedAt, original.UploadedAt);

        var supplement = reloaded.Attachments.Single(a => a.Id == supplementAttachmentId);
        Assert.Equal(reloaded.SupplementRequests.Single().Id, supplement.SupplementRequestId);
        Assert.Equal("地圖.pdf", supplement.FileName);
        Assert.Equal(Now.AddDays(1), supplement.UploadedAt);
    }

    // ── 存取範圍：範圍要真的變成 SQL 的查詢條件 ─────────────

    [Fact]
    public async Task GetByIdAsync_AllScope_FindsCase()
    {
        var caseId = await SaveCaseAsync(DepartmentSeed.PublicWorks);

        Assert.NotNull(await FindAsync(caseId, CaseAccessScope.All));
    }

    // 正面對照：同一個單位找得到。沒有這個測試的話，下面的 null 可能只是因為「單位範圍什麼都查不到」
    [Fact]
    public async Task GetByIdAsync_SameDepartmentScope_FindsCase()
    {
        var caseId = await SaveCaseAsync(DepartmentSeed.PublicWorks);

        Assert.NotNull(await FindAsync(caseId, CaseAccessScope.ForDepartment(DepartmentSeed.PublicWorks)));
    }

    [Fact]
    public async Task GetByIdAsync_OtherDepartmentScope_ReturnsNull()
    {
        var caseId = await SaveCaseAsync(DepartmentSeed.PublicWorks);

        Assert.Null(await FindAsync(caseId, CaseAccessScope.ForDepartment(DepartmentSeed.Transportation)));
    }

    // 未分派的案件 DepartmentId 是 NULL；SQL 的 NULL = @id 是 UNKNOWN，要確認 EF 翻譯出來的條件不會把它當成符合
    [Fact]
    public async Task GetByIdAsync_UnassignedCaseWithDepartmentScope_ReturnsNull()
    {
        var caseId = await SaveCaseAsync(departmentId: null);

        Assert.Null(await FindAsync(caseId, CaseAccessScope.ForDepartment(DepartmentSeed.PublicWorks)));
    }

    // 分派人員要看得到未分派的案件，才有辦法分派
    [Fact]
    public async Task GetByIdAsync_UnassignedCaseWithAllScope_FindsCase()
    {
        var caseId = await SaveCaseAsync(departmentId: null);

        Assert.NotNull(await FindAsync(caseId, CaseAccessScope.All));
    }

    [Fact]
    public async Task GetByIdAsync_NoneScope_ReturnsNull()
    {
        var caseId = await SaveCaseAsync(DepartmentSeed.PublicWorks);

        Assert.Null(await FindAsync(caseId, CaseAccessScope.None));
    }

    private static async Task<Guid> SaveCaseAsync(Guid? departmentId)
    {
        var @case = new Case(TestData.NewCaseNumber(), "路燈不亮", "中山路路燈故障", TestData.Petitioner, TestData.SubmittedBy, TestData.SubmittedAt, []);
        if (departmentId is { } id)
        {
            @case.AssignTo(id);
        }

        await using var writeScope = CreateScope();
        writeScope.ServiceProvider.GetRequiredService<ICaseRepository>().Add(@case);
        await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(TestContext.Current.CancellationToken);
        return @case.Id;
    }

    // 用另一個 scope 讀：確認查詢條件是在資料庫執行的，不是從記憶體裡拿到的
    private static async Task<Case?> FindAsync(Guid caseId, CaseAccessScope scope)
    {
        await using var readScope = CreateScope();
        return await readScope.ServiceProvider.GetRequiredService<ICaseRepository>()
            .GetByIdAsync(caseId, scope, TestContext.Current.CancellationToken);
    }

    // 每呼叫一次就模擬一個新的 HTTP 請求：裡面的 DbContext、Repository 都是全新的實例
    private static AsyncServiceScope CreateScope()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(DatabaseFixture.ConnectionString, TestData.FileStorageRoot);
        services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser("test-user"));
        return services.BuildServiceProvider().CreateAsyncScope();
    }
}
