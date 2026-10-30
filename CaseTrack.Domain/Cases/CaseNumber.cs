using System.Globalization;

namespace CaseTrack.Domain.Cases;

// 案件編號：送件日期（台灣）+ 當天的流水號，例如 20261029-0002。
// 編號的規則在這裡；流水號怎麼產生（資料庫計數器）是 Infrastructure 的事，換成 sequence 或 Redis 時規則不用跟著搬。
// private 建構子：只能透過 Create 建立，Case 收到的編號一定符合格式
public sealed record CaseNumber
{
    // 台灣沒有日光節約時間，固定 +8
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);

    public string Value { get; }

    private CaseNumber(string value)
    {
        Value = value;
    }

    // D4 是「至少 4 位，不足補 0」：當天第 10000 件會變成 5 位數，編號仍然唯一，不讓第 10000 個民眾送件失敗。
    // 指定 InvariantCulture：yyyy 會跟著執行緒的文化套用曆法，伺服器的文化設成泰國（佛曆）時年份會變成 2569
    public static CaseNumber Create(DateOnly date, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return new CaseNumber(string.Create(CultureInfo.InvariantCulture, $"{date:yyyyMMdd}-{sequence:D4}"));
    }

    // 編號用台灣的日期：UTC 的 10/28 17:00 是台灣的 10/29 凌晨 1 點，要算成 29 號的案件
    public static DateOnly DateOf(DateTimeOffset submittedAt) =>
        DateOnly.FromDateTime(submittedAt.ToOffset(TaiwanOffset).DateTime);

    public override string ToString() => Value;
}
