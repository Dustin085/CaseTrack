using CaseTrack.Domain.Departments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CaseTrack.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");

        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Code).HasMaxLength(10);
        builder.HasIndex(d => d.Code).IsUnique();
        builder.Property(d => d.Name).HasMaxLength(50);

        builder.HasData(
            new { Id = DepartmentSeed.PublicWorks, Code = "PWD", Name = "工務局", IsActive = true },
            new { Id = DepartmentSeed.EnvironmentalProtection, Code = "ENV", Name = "環境保護局", IsActive = true },
            new { Id = DepartmentSeed.Transportation, Code = "TRA", Name = "交通局", IsActive = true });
    }
}
