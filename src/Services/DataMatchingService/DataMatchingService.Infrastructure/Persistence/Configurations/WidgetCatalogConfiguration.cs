using Hdos.DataMatchingService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DataMatchingService.Infrastructure.Persistence.Configurations;

public sealed class WidgetCatalogConfiguration : IEntityTypeConfiguration<WidgetCatalog>
{
    public void Configure(EntityTypeBuilder<WidgetCatalog> b)
    {
        b.ToTable("WidgetCatalogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ChartType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Category).HasMaxLength(50).IsRequired();
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Icon).HasMaxLength(100).IsRequired();
        b.Property(x => x.RowSchema).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.RequiredColumnsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OptionalColumnsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CompatibleWithJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.SortOrder).IsRequired();

        b.HasIndex(x => x.ChartType).IsUnique();
        b.HasIndex(x => x.Category);
    }
}
