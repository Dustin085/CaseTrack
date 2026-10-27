using CaseTrack.Application.Authorization;
using CaseTrack.Application.Cases;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

// 允許的動作 = 角色 ∩ 狀態。狀態的部分由 Domain 的一致性測試保證（CaseCapabilityConsistencyTests），
// 這裡測的是「角色」：哪一組角色拿得到哪些動作、多個角色時要合併、其他角色一律沒有
public class CaseActionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly Guid DepartmentId = Guid.NewGuid();

    [Fact]
    public void AllowedFor_OfficerOnCaseUnderReview_ReturnsHandlerActionsOnly()
    {
        var actions = CaseActions.AllowedFor(UnderReview(), User(Roles.CaseOfficer));

        Assert.Equal(
            [CaseAction.RequestSupplement, CaseAction.Close, CaseAction.Reject, CaseAction.ViewPetitioner],
            actions);
    }

    // 主管跟承辦人同一組（Roles.CaseHandlers）
    [Fact]
    public void AllowedFor_SupervisorOnAssignedSubmittedCase_CanReview()
    {
        var actions = CaseActions.AllowedFor(Assigned(NewCase()), User(Roles.Supervisor));

        Assert.Equal([CaseAction.Review, CaseAction.Reject, CaseAction.ViewPetitioner], actions);
    }

    // 分派人員看得到案件，但不能看完整個資、不能處理
    [Fact]
    public void AllowedFor_DispatcherOnUnassignedCase_ReturnsAssignAndDismissOnly()
    {
        var actions = CaseActions.AllowedFor(NewCase(), User(Roles.Dispatcher));

        Assert.Equal([CaseAction.Assign, CaseAction.Dismiss], actions);
    }

    // 已結案：狀態上什麼都不能做了，只剩查看個資（只看角色，不看狀態）
    [Fact]
    public void AllowedFor_OfficerOnClosedCase_ReturnsViewPetitionerOnly()
    {
        var @case = UnderReview();
        @case.Close("已處理");

        var actions = CaseActions.AllowedFor(@case, User(Roles.CaseOfficer));

        Assert.Equal([CaseAction.ViewPetitioner], actions);
    }

    // 多個角色：兩組各自判斷，結果合併
    [Fact]
    public void AllowedFor_DispatcherWhoIsAlsoOfficer_ReturnsBothGroups()
    {
        var actions = CaseActions.AllowedFor(
            Assigned(NewCase()), User(Roles.CaseOfficer, Roles.Dispatcher));

        Assert.Equal(
            [CaseAction.Review, CaseAction.Reject, CaseAction.ViewPetitioner, CaseAction.Assign, CaseAction.Dismiss],
            actions);
    }

    // 預設拒絕：管理者、民眾、不認識的角色都沒有任何動作。
    // 故意讓他們有單位：如果程式誤把他們當成承辦人，就會拿到動作
    [Theory]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.Citizen)]
    [InlineData("Auditor")]
    public void AllowedFor_OtherRoles_ReturnsNothing(string role)
    {
        var actions = CaseActions.AllowedFor(Assigned(NewCase()), User(role));

        Assert.Empty(actions);
    }

    // ── Helpers ──────────────────────────────────────────

    private static FixedCurrentUser User(params string[] roles) =>
        new("u-test", roles.ToHashSet(), DepartmentId);

    private static Case NewCase() =>
        new("20261005-0001", "路燈不亮", "中山路路燈故障",
            new Petitioner("王小明", "0912345678", null, null), "u-citizen-1", Now, []);

    private static Case Assigned(Case @case)
    {
        @case.AssignTo(DepartmentId);
        return @case;
    }

    private static Case UnderReview()
    {
        var @case = Assigned(NewCase());
        @case.StartReview();
        return @case;
    }
}
