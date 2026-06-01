using Hdos.DynamicFormService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Configurations;

public sealed class FormFieldConfiguration : IEntityTypeConfiguration<FormField>
{
    public void Configure(EntityTypeBuilder<FormField> b)
    {
        b.ToTable("FormFields");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.FormTemplateId).IsRequired();
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.Property(x => x.FieldType).HasMaxLength(30).HasConversion<string>().IsRequired();
        b.Property(x => x.Order).IsRequired();
        b.Property(x => x.Required).IsRequired();
        b.Property(x => x.Width).HasMaxLength(10).HasConversion<string>().IsRequired();
        b.Property(x => x.Placeholder).HasMaxLength(300);
        b.Property(x => x.HelpText).HasMaxLength(500);
        b.Property(x => x.OptionsJson).HasColumnType("jsonb");
        b.Property(x => x.ValidationRulesJson).HasColumnType("jsonb");
        b.Property(x => x.ConditionalLogicJson).HasColumnType("jsonb");
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.FormTemplateId, x.Key }).IsUnique();
        b.HasIndex(x => new { x.FormTemplateId, x.Order });
    }
}
