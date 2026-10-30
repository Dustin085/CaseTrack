using System.Globalization;
using CaseTrack.Domain.Cases;

namespace CaseTrack.Tests;

// 案件編號的規則：日期 + 至少 4 位的流水號，日期是台灣的日期
public class CaseNumberTests
{
    [Theory]
    [InlineData(1, "20261029-0001")]
    [InlineData(42, "20261029-0042")]
    [InlineData(9999, "20261029-9999")]
    [InlineData(10000, "20261029-10000")] // D4 是最少 4 位，不是最多：當天第 10000 件照樣可以送件
    public void Create_FormatsDateAndPaddedSequence(int sequence, string expected)
    {
        var number = CaseNumber.Create(new DateOnly(2026, 10, 29), sequence);

        Assert.Equal(expected, number.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithSequenceBelowOne_Throws(int sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CaseNumber.Create(new DateOnly(2026, 10, 29), sequence));
    }

    // 執行緒的文化是泰國時，yyyy 會用佛曆（2569）；編號一定要是西元
    [Fact]
    public void Create_UnderBuddhistCalendarCulture_StillUsesGregorianYear()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            var number = CaseNumber.Create(new DateOnly(2026, 10, 29), 1);

            Assert.Equal("20261029-0001", number.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // record 的值相等：同一天、同一號就是同一個編號
    [Fact]
    public void Create_SameDateAndSequence_AreEqual()
    {
        Assert.Equal(CaseNumber.Create(new DateOnly(2026, 10, 29), 1), CaseNumber.Create(new DateOnly(2026, 10, 29), 1));
    }

    // UTC 10/28 17:00 = 台灣 10/29 凌晨 1:00，要算成 29 號
    [Fact]
    public void DateOf_BeforeEightAmTaiwanTime_UsesTaiwanDate()
    {
        var date = CaseNumber.DateOf(new DateTimeOffset(2026, 10, 28, 17, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 29), date);
    }

    // 台灣時間 23:59 還是當天
    [Fact]
    public void DateOf_JustBeforeTaiwanMidnight_UsesSameDay()
    {
        var date = CaseNumber.DateOf(new DateTimeOffset(2026, 10, 29, 23, 59, 59, TimeSpan.FromHours(8)));

        Assert.Equal(new DateOnly(2026, 10, 29), date);
    }
}
