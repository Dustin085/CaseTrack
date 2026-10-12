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

        // 樂觀併發控制：SQL Server 在每次 UPDATE 時自動更新這個值，EF 會把讀出時的值放進 UPDATE 的 WHERE 條件。
        // 值不一致代表讀出之後已被別人修改，EF 會丟出 DbUpdateConcurrencyException。
        // 用影子屬性，因為這是資料庫的技術細節，Domain 不需要知道。
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
