using Hdos.DynamicFormService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Configurations;

public sealed class FormTemplateConfiguration : IEntityTypeConfiguration<FormTemplate>
{
    public void Configure(EntityTypeBuilder<FormTemplate> b)
    {
        b.ToTable("FormTemplates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ModuleId).IsRequired();
        b.Property(x => x.ModuleCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Status).HasMaxLength(20).HasConversion<string>().IsRequired();
        b.Property(x => x.Version).IsRequired();
        b.Property(x => x.SettingsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasMany(x => x.Fields)
            .WithOne()
            .HasForeignKey(f => f.FormTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ModuleId, x.Key }).IsUnique();
        b.HasIndex(x => x.ModuleCode);
        b.HasIndex(x => x.Status);

        b.Ignore(x => x.DomainEvents);
    }
}
