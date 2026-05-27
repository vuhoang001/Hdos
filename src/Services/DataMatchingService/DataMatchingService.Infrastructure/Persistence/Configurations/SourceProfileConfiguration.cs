using Hdos.DataMatchingService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DataMatchingService.Infrastructure.Persistence.Configurations;

public sealed class SourceProfileConfiguration : IEntityTypeConfiguration<SourceProfile>
{
    public void Configure(EntityTypeBuilder<SourceProfile> b)
    {
        b.ToTable("SourceProfiles");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.SourceSystem)
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.BusinessKeyField)
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.FieldMappingsJson)
            .HasColumnType("text")
            .IsRequired();

        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.SourceSystem).IsUnique();
    }
}
