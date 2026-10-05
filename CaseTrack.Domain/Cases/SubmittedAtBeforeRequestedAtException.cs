using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CaseTrack.Domain.Cases
{
    public class SubmittedAtBeforeRequestedAtException : DomainException
    {
        public DateTimeOffset SubmittedAt { get; }
        public DateTimeOffset RequestedAt { get; }
        public SubmittedAtBeforeRequestedAtException(DateTimeOffset submittedAt, DateTimeOffset requestedAt) 
            : base($"SubmittedAt cannot be earlier than RequestedAt.SubmittedAt: {submittedAt}, RequestedAt: {requestedAt}")
        {
            SubmittedAt = submittedAt;
            RequestedAt = requestedAt;
        }
    }
}
