namespace CaseTrack.Application.Cases;

// 承辦端可以對案件做的動作。GET /api/cases/{id} 的 allowedActions 會列出目前使用者「現在」能做哪些，
// 前端照著顯示按鈕，不用自己知道規則。
// 名稱就是 API 的合約（JSON 輸出成 "Close"，前端也寫 'Close'），改名等於改了 API
public enum CaseAction
{
    // 承辦人、主管（Roles.CaseHandlers）
    Review,
    RequestSupplement,
    Close,
    Reject,
    ViewPetitioner,

    // 分派人員（Roles.CaseDispatchers）
    Assign,
    Dismiss,
}
