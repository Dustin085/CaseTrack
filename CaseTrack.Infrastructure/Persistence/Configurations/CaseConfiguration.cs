using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence.Configurations;

public class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("Cases");

        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.CaseNumber).HasMaxLength(20);
        builder.HasIndex(c => c.CaseNumber).IsUnique();

        builder.Property(c => c.Subject).HasMaxLength(200);

        // Content 刻意維持 nvarchar(max)：陳情內容長度不固定，不設上限

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);

        builder.Property(c => c.RejectionReason).HasMaxLength(500);

        builder.HasMany(c => c.SupplementRequests)
            .WithOne()
            .HasForeignKey("CaseId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
