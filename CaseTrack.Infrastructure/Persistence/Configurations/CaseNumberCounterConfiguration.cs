using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence.Configurations;

public class CaseNumberCounterConfiguration : IEntityTypeConfiguration<CaseNumberCounter>
{
    public void Configure(EntityTypeBuilder<CaseNumberCounter> builder)
    {
        builder.ToTable("CaseNumberCounters");

        builder.HasKey(c => c.Date);
    }
}