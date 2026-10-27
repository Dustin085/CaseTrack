using CaseTrack.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");

        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.FileName).HasMaxLength(AttachmentRules.MaxFileNameLength);
        builder.Property(a => a.ContentType).HasMaxLength(100);

        // 補件的附件指向那一次補件。用 Restrict 不用 Cascade：Cases 已經會連帶刪除 SupplementRequests 和 Attachments，
        // 再加一條 SupplementRequests → Attachments 的連帶刪除就有兩條路徑，SQL Server 不允許（multiple cascade paths）
        builder.HasOne<SupplementRequest>()
            .WithMany()
            .HasForeignKey(a => a.SupplementRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
