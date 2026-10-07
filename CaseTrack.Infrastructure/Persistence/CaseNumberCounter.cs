namespace CaseTrack.Infrastructure.Persistence;

public class CaseNumberCounter
{
    public DateOnly Date { get; private set; }
    public int LastValue { get; private set; }
}
