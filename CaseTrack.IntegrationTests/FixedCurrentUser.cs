using System.Collections.Frozen;
using CaseTrack.Application.Abstractions;

namespace CaseTrack.IntegrationTests;

// roles 的預設值只能是 null：參數的預設值必須是編譯期常數，FrozenSet<string>.Empty 不是
internal sealed class FixedCurrentUser(
    string fixedUserId,
    IReadOnlySet<string>? roles = null,
    Guid? departmentId = null) : ICurrentUser
{
    public string UserId => fixedUserId;

    public IReadOnlySet<string> Roles => roles ?? FrozenSet<string>.Empty;

    public Guid? DepartmentId => departmentId;
}
