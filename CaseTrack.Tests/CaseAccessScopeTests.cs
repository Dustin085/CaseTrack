using CaseTrack.Application.Authorization;

namespace CaseTrack.Tests;

public class CaseAccessScopeTests
{
    private static readonly Guid DepartmentId = Guid.NewGuid();

    // ── For：依照角色決定範圍 ─────────────────────────────

    [Fact]
    public void For_RoleDispatcher_ReturnsAll()
    {
        var user = new FixedCurrentUser(
            "Test User",
            new HashSet<string> { Roles.Dispatcher });

        Assert.Equal(CaseAccessScope.All, CaseAccessScope.For(user));
    }

    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.CaseOfficer)]
    public void For_OfficerOrSupervisorWithDepartment_ReturnsThatDepartment(string role)
    {
        var departmentId = Guid.NewGuid();
        var user = new FixedCurrentUser(
            "Test User",
            new HashSet<string> { role },
            departmentId);

        Assert.Equal(CaseAccessScope.ForDepartment(departmentId), CaseAccessScope.For(user));
    }

    // 承辦人沒有單位是 IdP 的設定錯誤：往安全的那一邊錯，什麼都看不到
    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.CaseOfficer)]
    public void For_OfficerOrSupervisorWithoutDepartment_ReturnsNone(string role)
    {
        var user = new FixedCurrentUser("Test User", new HashSet<string> { role });

        Assert.Equal(CaseAccessScope.None, CaseAccessScope.For(user));
    }

    // 管理者要「有單位」：沒有單位的話，就算程式把管理者當成承辦人處理，結果也剛好是 None，抓不到錯誤
    [Fact]
    public void For_AdministratorWithDepartment_ReturnsNone()
    {
        var user = new FixedCurrentUser(
            "Test User",
            new HashSet<string> { Roles.Administrator },
            DepartmentId);

        Assert.Equal(CaseAccessScope.None, CaseAccessScope.For(user));
    }

    // 沒有登入的民眾、排程：沒有任何角色
    [Fact]
    public void For_WithoutRoles_ReturnsNone()
    {
        var user = new FixedCurrentUser("anonymous");

        Assert.Equal(CaseAccessScope.None, CaseAccessScope.For(user));
    }

    // 多個角色取比較大的範圍：先檢查承辦人的話，他就只看得到本單位，做不了分派
    [Fact]
    public void For_DispatcherWhoIsAlsoOfficer_ReturnsAll()
    {
        var user = new FixedCurrentUser(
            "Test User",
            new HashSet<string> { Roles.CaseOfficer, Roles.Dispatcher },
            DepartmentId);

        Assert.Equal(CaseAccessScope.All, CaseAccessScope.For(user));
    }

    // 預設拒絕：系統不認識的角色（例如之後新增、卻忘了在 For 裡處理的角色）什麼都看不到
    [Fact]
    public void For_UnknownRole_ReturnsNone()
    {
        var user = new FixedCurrentUser(
            "Test User",
            new HashSet<string> { "Auditor" },
            DepartmentId);

        Assert.Equal(CaseAccessScope.None, CaseAccessScope.For(user));
    }

    // 民眾在承辦端看不到任何案件。故意讓他「有單位」：如果程式把民眾當成承辦人處理，會拿到單位範圍而失敗
    [Fact]
    public void For_CitizenWithDepartment_ReturnsNone()
    {
        var user = new FixedCurrentUser(
            "Test User",
            new HashSet<string> { Roles.Citizen },
            DepartmentId);

        Assert.Equal(CaseAccessScope.None, CaseAccessScope.For(user));
    }

    // ── 建立範圍 ─────────────────────────────────────────

    [Fact]
    public void ForDepartment_EmptyDepartmentId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CaseAccessScope.ForDepartment(Guid.Empty));
    }

    // record 的值相等：同一個單位的範圍是相等的，不同單位不相等
    [Fact]
    public void ForDepartment_SameDepartment_AreEqual()
    {
        Assert.Equal(CaseAccessScope.ForDepartment(DepartmentId), CaseAccessScope.ForDepartment(DepartmentId));
        Assert.NotEqual(CaseAccessScope.ForDepartment(DepartmentId), CaseAccessScope.ForDepartment(Guid.NewGuid()));
    }

    [Fact]
    public void ForPetitioner_ValidUserId_ReturnsPetitionerScope()
    {
        var scope = CaseAccessScope.ForPetitioner("u-citizen-1");

        Assert.Equal(CaseAccessScopeKind.Petitioner, scope.Kind);
        Assert.Equal("u-citizen-1", scope.PetitionerId);
        Assert.Null(scope.DepartmentId);
    }

    // 空的 userId 會產生「SubmittedBy == ''」的條件，不能讓它建立出來。
    // ThrowsAny：null 拋的是 ArgumentNullException（ArgumentException 的子類別），Throws 要求型別完全相同
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForPetitioner_BlankUserId_ThrowsArgumentException(string? userId)
    {
        Assert.ThrowsAny<ArgumentException>(() => CaseAccessScope.ForPetitioner(userId!));
    }

    [Fact]
    public void ForPetitioner_SameUser_AreEqual()
    {
        Assert.Equal(CaseAccessScope.ForPetitioner("u-1"), CaseAccessScope.ForPetitioner("u-1"));
        Assert.NotEqual(CaseAccessScope.ForPetitioner("u-1"), CaseAccessScope.ForPetitioner("u-2"));
    }

    // enum 的預設值是數值 0，而 0 必須代表 None：哪裡忘了設定時，得到的是「什麼都看不到」
    [Fact]
    public void Kind_DefaultValue_IsNone()
    {
        Assert.Equal(CaseAccessScopeKind.None, default(CaseAccessScopeKind));
    }
}
