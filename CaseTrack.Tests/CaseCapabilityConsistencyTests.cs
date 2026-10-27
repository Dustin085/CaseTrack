using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

// Case 的 CanXxx（能不能做）必須跟動作本身（做了會不會成功）完全一致。
// 動作和 API 的 allowedActions 都讀 CanXxx，規則只寫一次；這組測試保證以後改了其中一邊，
// 另一邊沒跟著改就會失敗。所有「情境 × 動作」的組合都測，不挑代表性的幾個
public class CaseCapabilityConsistencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly Petitioner Petitioner = new("王小明", "0912345678", null, null);

    // 案件可能處於的情境。「已送出」分成兩種：受理除了狀態，還要求已經分派
    private static readonly Dictionary<string, Func<Case>> Scenarios = new()
    {
        ["已送出、未分派"] = () => NewCase(),
        ["已送出、已分派"] = () => Assigned(NewCase()),
        ["審核中"] = () => UnderReview(),
        ["補件中"] = () =>
        {
            var @case = UnderReview();
            @case.RequestSupplement("請補照片", Now);
            return @case;
        },
        ["已結案"] = () =>
        {
            var @case = UnderReview();
            @case.Close("已處理");
            return @case;
        },
        ["已退件"] = () =>
        {
            var @case = NewCase();
            @case.Reject("非本單位業務");
            return @case;
        },
    };

    // 每個動作：判斷屬性，以及實際執行的方式（參數都是合法的，失敗只可能是因為狀態）
    private static readonly Dictionary<string, (Func<Case, bool> Can, Action<Case> Act)> Actions = new()
    {
        ["StartReview"] = (c => c.CanStartReview, c => c.StartReview()),
        ["RequestSupplement"] = (c => c.CanRequestSupplement, c => c.RequestSupplement("請補照片", Now)),
        ["SubmitSupplement"] = (c => c.CanSubmitSupplement, c => c.SubmitSupplement("已補上照片", Now, [])),
        ["Close"] = (c => c.CanClose, c => c.Close("已處理")),
        ["Reject"] = (c => c.CanReject, c => c.Reject("非本單位業務")),
        ["Dismiss"] = (c => c.CanDismiss, c => c.Dismiss("不屬本府權責")),
        ["Assign"] = (c => c.CanAssign, c => c.AssignTo(Guid.NewGuid())),
    };

    public static TheoryData<string, string> AllCombinations()
    {
        var data = new TheoryData<string, string>();
        foreach (var scenario in Scenarios.Keys)
        {
            foreach (var action in Actions.Keys)
            {
                data.Add(scenario, action);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void CanXxx_MatchesWhetherActionSucceeds(string scenario, string action)
    {
        var (can, act) = Actions[action];
        var @case = Scenarios[scenario]();

        var predicted = can(@case);
        var succeeded = TrySucceeds(() => act(@case));

        Assert.True(predicted == succeeded,
            $"{scenario}：Can{action} 是 {predicted}，但實際執行{(succeeded ? "成功了" : "失敗了")}");
    }

    // 新增狀態時，這裡會失敗，提醒要補上那個狀態的情境，不然新狀態的規則完全沒被測到
    [Fact]
    public void Scenarios_CoverEveryStatus()
    {
        var covered = Scenarios.Values.Select(create => create().Status).ToHashSet();

        Assert.Equal(Enum.GetValues<CaseStatus>().ToHashSet(), covered);
    }

    // 判斷屬性只能讀，不能改到案件
    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void CanXxx_DoesNotChangeCase(string scenario, string action)
    {
        var @case = Scenarios[scenario]();
        var statusBefore = @case.Status;

        _ = Actions[action].Can(@case);

        Assert.Equal(statusBefore, @case.Status);
    }

    // ── Helpers ──────────────────────────────────────────

    private static Case NewCase() =>
        new("20261005-0001", "路燈不亮", "中山路路燈故障", Petitioner, "u-citizen-1", Now, []);

    private static Case Assigned(Case @case)
    {
        @case.AssignTo(Guid.NewGuid());
        return @case;
    }

    private static Case UnderReview()
    {
        var @case = Assigned(NewCase());
        @case.StartReview();
        return @case;
    }

    // 動作被 Domain 拒絕時丟的例外。只接住這些：其他例外（例如程式錯誤）要讓測試直接失敗，
    // 不能被當成「動作不允許」而讓測試意外通過
    private static bool TrySucceeds(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (e is InvalidCaseStatusTransitionException
                                       or CaseNotAssignedException
                                       or CaseAssignmentNotAllowedException)
        {
            return false;
        }
    }
}
