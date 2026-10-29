using System.Collections.Frozen;

namespace CaseTrack.Application.Authorization;

// 系統的角色。放在 Application：「哪個角色能看哪些案件」是業務規則，之後的 CaseAccessScope 會用到。
// 值會出現在 token 的 role claim 裡，改名等於改了跟 IdP 之間的約定。
public static class Roles
{
    public const string CaseOfficer = "CaseOfficer";        // 承辦人：處理本單位的案件
    public const string Supervisor = "Supervisor";          // 單位主管：處理本單位的案件
    public const string Dispatcher = "Dispatcher";          // 分派人員：把案件分派到單位
    public const string Administrator = "Administrator";    // 管理者：維護系統設定，不能看案件內容
    public const string Citizen = "Citizen";                // 民眾：送出陳情的人

    // FrozenSet：這些集合決定整個系統的授權，而且全域共用。HashSet 宣告成 IReadOnlySet 還是能轉型回去 Add，
    // FrozenSet 真的不能修改。角色的分組只定義在這裡：Program.cs 的 Policy、CaseAccessScope、
    // 允許的動作（CaseActions）都用它們，新增角色只要改一個地方
    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { CaseOfficer, Supervisor, Dispatcher, Administrator, Citizen }.ToFrozenSet();

    // 處理案件：受理、要求補件、結案、退件、查看完整個資（Policy：CaseHandler）
    public static readonly IReadOnlySet<string> CaseHandlers =
        new HashSet<string> { CaseOfficer, Supervisor }.ToFrozenSet();

    // 核定或退回承辦人送出的結案（Policy：ClosingApprover）。主管同時也在 CaseHandlers（可以自己承辦），
    // 但不能核定自己送出的結案（Domain 的 SelfApprovalNotAllowedException）
    public static readonly IReadOnlySet<string> ClosingApprovers =
        new HashSet<string> { Supervisor }.ToFrozenSet();

    // 分派、不予受理（Policy：CaseDispatcher）
    public static readonly IReadOnlySet<string> CaseDispatchers =
        new HashSet<string> { Dispatcher }.ToFrozenSet();

    // 查詢案件：處理和分派的人都要看得到（Policy：CaseReader）。由兩組合併而來，不另外列角色。
    // 必須寫在 CaseHandlers、CaseDispatchers 後面：靜態欄位依宣告順序初始化
    public static readonly IReadOnlySet<string> CaseReaders =
        CaseHandlers.Union(CaseDispatchers).ToFrozenSet();
}
