namespace CaseTrack.Application.Abstractions;

// 目前執行這個操作的是誰。Application 不知道使用者是從哪裡來的（token、權限表、排程），
// 之後角色改成查自己的權限表，只要換掉實作，用到它的程式碼都不用改。
public interface ICurrentUser
{
    // 唯一、不會變的使用者 Id（token 的 sub），稽核記錄用它追查責任；姓名會重複、會改，不能用來識別人。
    // 排程是 "system"。沒有登入是 "anonymous"：會寫入資料的端點都要登入（民眾也是），出現 anonymous 代表有端點漏了 [Authorize]。
    string UserId { get; }

    // token 裡所有的角色（Application.Authorization.Roles 的值）。排程、沒有登入：空集合，不是 null
    IReadOnlySet<string> Roles { get; }

    // 所屬的承辦單位。分派人員、管理者、民眾、排程都沒有：null
    Guid? DepartmentId { get; }
}
