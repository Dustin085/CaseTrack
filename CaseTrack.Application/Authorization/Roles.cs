namespace CaseTrack.Application.Authorization;

// 系統的角色。放在 Application：「哪個角色能看哪些案件」是業務規則，之後的 CaseAccessScope 會用到。
// 值會出現在 token 的 role claim 裡，改名等於改了跟 IdP 之間的約定。
public static class Roles
{
    public const string CaseOfficer = "CaseOfficer";        // 承辦人：處理本單位的案件
    public const string Supervisor = "Supervisor";          // 單位主管：處理本單位的案件
    public const string Dispatcher = "Dispatcher";          // 分派人員：把案件分派到單位
    public const string Administrator = "Administrator";    // 管理者：維護系統設定，不能看案件內容

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { CaseOfficer, Supervisor, Dispatcher, Administrator };
}
