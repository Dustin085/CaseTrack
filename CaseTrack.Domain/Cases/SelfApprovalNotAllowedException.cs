namespace CaseTrack.Domain.Cases;

// 職責分離：送出結案的人不能自己核定（主管自己承辦的案件，要由另一位主管核定）
public sealed class SelfApprovalNotAllowedException : DomainException
{
    public SelfApprovalNotAllowedException()
        : base("不能核定自己送出的結案，請由其他主管核定")
    {
    }
}
