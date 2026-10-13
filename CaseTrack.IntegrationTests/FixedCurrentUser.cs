using CaseTrack.Application.Abstractions;

namespace CaseTrack.IntegrationTests;

internal sealed class FixedCurrentUser(string fixedName) : ICurrentUser
{
    public string Name => fixedName;
}
