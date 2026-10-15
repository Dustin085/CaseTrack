namespace CaseTrack.Application.Exceptions;

public sealed class RequestValidationException : Exception
{
    public string Field { get; }
    // 建構子：欄位名稱 + 訊息
    public RequestValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }
}
