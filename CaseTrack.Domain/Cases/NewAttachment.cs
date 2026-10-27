namespace CaseTrack.Domain.Cases;

// 要新增的附件，只有中繼資料。檔案內容（Stream）不進 Domain：Domain 只負責「能不能加、加幾個」，存檔是 Infrastructure 的事。
// ContentType 由呼叫端（Application）檢查過檔頭之後決定，不是瀏覽器送來的值
public sealed record NewAttachment(string FileName, string ContentType, long Size);
