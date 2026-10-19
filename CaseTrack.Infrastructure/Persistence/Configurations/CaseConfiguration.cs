using CaseTrack.Domain.Cases;
using CaseTrack.Domain.Departments;
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
        builder.Property(c => c.ClosingReply).HasMaxLength(2000);

        // 長度跟稽核的 ChangedBy 一致；民眾查「我的案件」用它過濾，所以加索引
        builder.Property(c => c.SubmittedBy).HasMaxLength(100);
        builder.HasIndex(c => c.SubmittedBy);

        // 值物件用 ComplexProperty：欄位攤平在 Cases 表（Petitioner_Name…），EF 把它當成 Case 的一部分。
        // 不用 OwnsOne：Owned Type 其實是有隱藏主鍵的 entity，整個換掉值物件時會被追蹤成「刪除舊的 + 新增新的」。
        builder.ComplexProperty(c => c.Petitioner, petitioner =>
        {
            // IsPersonalData：稽核紀錄裡會遮罩
            petitioner.Property(p => p.Name).HasMaxLength(50).IsPersonalData();
            petitioner.Property(p => p.Phone).HasMaxLength(20).IsPersonalData();
            petitioner.Property(p => p.Email).HasMaxLength(254).IsPersonalData();
            petitioner.Property(p => p.Address).HasMaxLength(200).IsPersonalData();
        });

        builder.HasMany(c => c.SupplementRequests)
            .WithOne()
            .HasForeignKey("CaseId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Case 只存單位的 Id（聚合之間只用 Id 參照），但資料庫仍然要保證這個 Id 存在
        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(c => c.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 樂觀併發控制：SQL Server 在每次 UPDATE 時自動更新這個值，EF 會把讀出時的值放進 UPDATE 的 WHERE 條件。
        // 值不一致代表讀出之後已被別人修改，EF 會丟出 DbUpdateConcurrencyException。
        // 用影子屬性，因為這是資料庫的技術細節，Domain 不需要知道。
        builder.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
