namespace CaseTrack.Application.Abstractions;

// 目前執行這個操作的是誰。Application 不知道使用者是從哪裡來的（token、權限表、排程），
// 之後角色改成查自己的權限表，只要換掉實作，用到它的程式碼都不用改。
public interface ICurrentUser
{
    // 唯一、不會變的使用者 Id（token 的 sub），稽核記錄用它追查責任；姓名會重複、會改，不能用來識別人。
    // 排程是 "system"，沒有登入的民眾是 "anonymous"。
    string UserId { get; }
}
