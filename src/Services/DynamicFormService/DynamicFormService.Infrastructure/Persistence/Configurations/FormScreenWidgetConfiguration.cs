using Hdos.DynamicFormService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Configurations;

public sealed class FormScreenWidgetConfiguration : IEntityTypeConfiguration<FormScreenWidget>
{
    public void Configure(EntityTypeBuilder<FormScreenWidget> b)
    {
        b.ToTable("FormScreenWidgets");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TabId).IsRequired();
        b.Property(x => x.WidgetKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.WidgetType).HasMaxLength(30).HasConversion<string>().IsRequired();
        b.Property(x => x.GridX).IsRequired();
        b.Property(x => x.GridY).IsRequired();
        b.Property(x => x.GridW).IsRequired();
        b.Property(x => x.GridH).IsRequired();
        b.Property(x => x.ConfigJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ReferenceId);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TabId, x.WidgetKey }).IsUnique();
        b.HasIndex(x => x.TabId);
    }
}
