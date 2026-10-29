using CaseTrack.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        // 跟 Case 不同：稽核紀錄的主鍵由資料庫自動遞增（IDENTITY），順序就是寫入的順序
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(50);
        builder.Property(a => a.Action).HasMaxLength(20);
        builder.Property(a => a.ChangedBy).HasMaxLength(100);

        // Changes 刻意維持 nvarchar(max)：內容是 JSON，一次修改的欄位數量不固定

        // 最常見的查詢：「某一筆資料的所有異動紀錄」
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        // 管理者的稽核查詢預設依時間新到舊、可以篩期間
        builder.HasIndex(a => a.ChangedAt);
    }
}
