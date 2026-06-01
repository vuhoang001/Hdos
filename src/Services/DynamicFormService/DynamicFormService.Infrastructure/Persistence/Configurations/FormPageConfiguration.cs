using Hdos.DynamicFormService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Configurations;

public sealed class FormPageConfiguration : IEntityTypeConfiguration<FormPage>
{
    public void Configure(EntityTypeBuilder<FormPage> b)
    {
        b.ToTable("FormPages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ModuleId).IsRequired();
        b.Property(x => x.ModuleCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Status).HasMaxLength(20).HasConversion<string>().IsRequired();
        b.Property(x => x.LayoutJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.ModuleCode, x.Code }).IsUnique();
        b.HasIndex(x => x.ModuleCode);
        b.HasIndex(x => x.Status);

        b.Ignore(x => x.DomainEvents);
    }
}
