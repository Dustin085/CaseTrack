namespace CaseTrack.Api.BackgroundJobs;

public sealed class OverdueScanOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public int BatchSize { get; set; } = 100;
}
