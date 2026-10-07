namespace CaseTrack.Application.Abstractions;

public interface ICaseNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
