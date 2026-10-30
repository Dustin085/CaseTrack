namespace CaseTrack.Application.Exceptions;

// 資料庫的唯一索引擋下了重複的值。通常是兩個請求同時新增同樣的值：先查詢的檢查兩邊都通過了，
// 存檔時才由資料庫擋下（最後一道防線）。
// 跟 ConcurrencyConflictException 一樣由 Infrastructure 轉換（SqlException 2601／2627），
// Application 和 Api 不需要認識資料庫的錯誤碼；換掉 EF 或資料庫時，新的實作負責轉成這個例外
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
