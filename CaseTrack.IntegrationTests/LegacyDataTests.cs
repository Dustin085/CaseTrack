using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace CaseTrack.IntegrationTests;

// 資料庫裡的舊資料，不一定符合目前 Domain 的規則（例如加上陳情人之前建立的案件），但仍然必須讀得出來
[Collection(nameof(DatabaseCollection))]
public class LegacyDataTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetCase_PetitionerViolatesCurrentRules_StillLoads()
    {
        // Arrange：直接用 SQL 寫入一筆 AddCasePetitioner migration 之前的案件（姓名是預設值 ''、沒有聯絡方式）
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var caseNumber = $"L-{Guid.NewGuid():N}"[..20];
        await using (var writeContext = fixture.CreateContext())
        {
            await writeContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO Cases (Id, CaseNumber, Subject, Content, Status, Petitioner_Name)
                VALUES ({id}, {caseNumber}, N'舊案件', N'舊案件內容', 'Submitted', '')
                """,
                cancellationToken);
        }

        // Act
        await using var readContext = fixture.CreateContext();
        var @case = await readContext.Cases.SingleAsync(c => c.Id == id, cancellationToken);

        // Assert：讀取不經過 Petitioner 建構子的驗證
        Assert.Equal(CaseStatus.Submitted, @case.Status);
        Assert.Equal("", @case.Petitioner.Name);
        Assert.Null(@case.Petitioner.Phone);
        Assert.Null(@case.Petitioner.Email);
    }
}
