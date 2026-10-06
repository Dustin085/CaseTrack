using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence.Configurations;

public class SupplementRequestConfiguration : IEntityTypeConfiguration<SupplementRequest>
{
    public void Configure(EntityTypeBuilder<SupplementRequest> builder)
    {
        builder.ToTable("SupplementRequests");

        builder.Property(sr => sr.Id).ValueGeneratedNever();

        builder.Property(sr => sr.Reason).HasMaxLength(500);

        builder.HasIndex("CaseId", nameof(SupplementRequest.Sequence)).IsUnique();
    }
}
