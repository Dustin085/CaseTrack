using CaseTrack.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence.Configurations;

public class PersonalDataAccessEntryConfiguration : IEntityTypeConfiguration<PersonalDataAccessEntry>
{
    public void Configure(EntityTypeBuilder<PersonalDataAccessEntry> builder)
    {
        builder.ToTable("PersonalDataAccessLogs");

        // 跟稽核紀錄一樣用 IDENTITY，順序就是寫入的順序
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.AccessedBy).HasMaxLength(100);

        // 最常見的兩種查詢：「某個人最近看了哪些民眾的資料」（調查是否濫用）、「這個案件的個資被誰看過」
        builder.HasIndex(e => new { e.AccessedBy, e.AccessedAt });
        builder.HasIndex(e => e.CaseId);
    }
}
