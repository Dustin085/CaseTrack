namespace CaseTrack.Application.Abstractions;

// 案件編號的流水號：某一天的下一個號碼（從 1 開始，同一天不重複）。
// 只負責「不重複的數字」，編號的格式和用哪一天由 Domain 的 CaseNumber 決定
public interface ICaseNumberSequence
{
    Task<int> NextAsync(DateOnly date, CancellationToken cancellationToken = default);
}
