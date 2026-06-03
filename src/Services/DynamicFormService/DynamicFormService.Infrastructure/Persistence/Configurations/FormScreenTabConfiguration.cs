using Hdos.DynamicFormService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Configurations;

public sealed class FormScreenTabConfiguration : IEntityTypeConfiguration<FormScreenTab>
{
    public void Configure(EntityTypeBuilder<FormScreenTab> b)
    {
        b.ToTable("FormScreenTabs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ScreenId).IsRequired();
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.IsDefault).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasMany(t => t.Widgets)
         .WithOne()
         .HasForeignKey(w => w.TabId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Navigation(t => t.Widgets).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => new { x.ScreenId, x.Slug }).IsUnique();
        b.HasIndex(x => x.ScreenId);
    }
}
