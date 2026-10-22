using CaseTrack.Application.Abstractions;

namespace CaseTrack.Application.Authorization;

// 明確寫出數值，不依賴成員的順序。None 必須是 0：enum 的預設值是 0（沒設定的欄位、default、缺少欄位的 JSON），
// 哪裡忘了設定時，得到的是「什麼都看不到」，而不是「全部」
public enum CaseAccessScopeKind
{
    None = 0,
    Department = 1,
    All = 2,
    Petitioner = 3,
}

// 使用者能看、能處理哪些案件（資料列層級授權）。Handler 依照目前的使用者建立，Repository 把它轉成查詢條件。
// 用獨立的型別而不是 Guid?：null 會同時代表「全部」（分派人員）和「沒有」（管理者），Repository 分辨不出來。
// private 建構子：只能透過 All、None、ForDepartment、ForPetitioner 建立，不可能出現「All 卻帶著單位」這種組合
public sealed record CaseAccessScope
{
    public CaseAccessScopeKind Kind { get; }
    public Guid? DepartmentId { get; }
    public string? PetitionerId { get; }

    private CaseAccessScope(CaseAccessScopeKind kind,
        Guid? departmentId = null,
        string? petitionerId = null)
    {
        Kind = kind;
        DepartmentId = departmentId;
        PetitionerId = petitionerId;
    }

    public static CaseAccessScope All { get; } = new(CaseAccessScopeKind.All);

    public static CaseAccessScope None { get; } = new(CaseAccessScopeKind.None);

    public static CaseAccessScope ForDepartment(Guid departmentId)
    {
        if (departmentId == Guid.Empty)
        {
            throw new ArgumentException("單位 Id 不能是空的", nameof(departmentId));
        }
        return new CaseAccessScope(CaseAccessScopeKind.Department, departmentId);
    }

    // 民眾只能看自己送出的案件（Case.SubmittedBy 就是送件時 token 的 sub）
    public static CaseAccessScope ForPetitioner(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return new CaseAccessScope(CaseAccessScopeKind.Petitioner, petitionerId: userId);
    }

    public static CaseAccessScope For(ICurrentUser user)
    {
        // 範圍大的角色先檢查：分派人員兼承辦人時取比較大的範圍，否則他就做不了分派
        if (user.Roles.Contains(Roles.Dispatcher))
        {
            return All;
        }

        // 承辦人、單位主管只能看本單位的案件。
        // 沒有單位是 IdP 的設定錯誤：往安全的那一邊錯，什麼都看不到，而不是全部都看得到
        if (user.Roles.Overlaps(Roles.CaseHandlers))
        {
            return user.DepartmentId is { } departmentId ? ForDepartment(departmentId) : None;
        }

        // 預設拒絕：管理者（最小權限，不能看案件內容）、民眾，以及之後新增卻忘了在這裡處理的角色。
        // 民眾不是漏掉了：這裡決定的是承辦端（/api/cases）的範圍。民眾端（/api/my/cases）一律用 ForPetitioner，
        // 由端點決定範圍，不合併角色，所以承辦人自己陳情時，兩邊各看各的，不需要「本單位 + 本人」的聯集
        return None;
    }
}
