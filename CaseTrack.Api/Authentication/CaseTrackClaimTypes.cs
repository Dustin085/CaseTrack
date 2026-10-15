namespace CaseTrack.Api.Authentication;

// token 裡的 claim 名稱：簽發端（IdP、/dev/token）和驗證端（Program.cs、HttpCurrentUser）要用同一組名稱
public static class CaseTrackClaimTypes
{
    public const string Subject = "sub";              // 使用者 Id：唯一、不會變，稽核記錄用它
    public const string Name = "name";                // 顯示名稱：會重複、會改
    public const string Role = "role";
    public const string DepartmentId = "department_id";
}
