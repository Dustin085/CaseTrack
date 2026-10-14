namespace CaseTrack.Domain;

// 資料本身不合法（例如沒填聯絡方式）：不管什麼時候送都不會成功。
// 跟其他 DomainException（跟目前的狀態衝突，例如已結案不能退件）分開，讓呼叫端可以分辨兩種錯誤；
// 要對應成哪個 HTTP 狀態碼由 Api 決定，Domain 只負責分類。
public abstract class DomainValidationException : DomainException
{
    protected DomainValidationException(string message) : base(message) { }
}
