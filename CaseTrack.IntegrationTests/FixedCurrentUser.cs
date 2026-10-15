using CaseTrack.Application.Abstractions;

namespace CaseTrack.IntegrationTests;

internal sealed class FixedCurrentUser(string fixedUserId) : ICurrentUser
{
    public string UserId => fixedUserId;
}
