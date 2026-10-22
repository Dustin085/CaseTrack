using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Authorization;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Application.Cases;

// 目前的使用者現在能對這件案件做哪些動作 = 角色允許 ∩ 案件的狀態允許。
// 兩條規則都不在這裡重寫：角色看 Roles 的分組（跟 Program.cs 的 Policy 同一份），
// 狀態看 Domain 的 CanXxx（跟動作本身同一份）。這裡只負責把兩者組合起來。
// 不用檢查「看不看得到這件案件」：呼叫端已經用 CaseAccessScope 查過，看不到的案件走不到這裡
public static class CaseActions
{
    public static IReadOnlyList<CaseAction> AllowedFor(Case @case, ICurrentUser user)
    {
        var actions = new List<CaseAction>();

        // 一個人可能同時有多個角色（例如分派人員兼承辦人）：兩組各自判斷，結果合併
        if (user.Roles.Overlaps(Roles.CaseHandlers))
        {
            AddIf(actions, @case.CanStartReview, CaseAction.Review);
            AddIf(actions, @case.CanRequestSupplement, CaseAction.RequestSupplement);
            AddIf(actions, @case.CanClose, CaseAction.Close);
            AddIf(actions, @case.CanReject, CaseAction.Reject);
            // 查看完整個資只看角色，不看狀態：結案後民眾來電詢問，承辦人也需要聯絡得到人
            actions.Add(CaseAction.ViewPetitioner);
        }

        if (user.Roles.Overlaps(Roles.CaseDispatchers))
        {
            AddIf(actions, @case.CanAssign, CaseAction.Assign);
            AddIf(actions, @case.CanDismiss, CaseAction.Dismiss);
        }

        // 預設拒絕：管理者、民眾、不認識的角色都不會進到上面任何一段，結果是空的
        return actions;
    }

    private static void AddIf(List<CaseAction> actions, bool allowed, CaseAction action)
    {
        if (allowed)
        {
            actions.Add(action);
        }
    }
}
