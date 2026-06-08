using Hdos.LakehouseService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.LakehouseService.Infrastructure.Persistence.Configurations;

public sealed class ViewBindingConfiguration : IEntityTypeConfiguration<ViewBinding>
{
    public void Configure(EntityTypeBuilder<ViewBinding> b)
    {
        b.ToTable("ViewBindings");
        b.HasKey(x => x.Id);

        b.Property(x => x.ViewName).HasMaxLength(200).IsRequired();
        b.Property(x => x.SourceSystem).HasMaxLength(200).IsRequired();
        b.Property(x => x.RecordType).HasMaxLength(100).IsRequired();
        b.Property(x => x.BusinessKeyColumn).HasMaxLength(100).IsRequired();
        b.Property(x => x.UpdatedAtColumn).HasMaxLength(100);
        b.Property(x => x.PollIntervalSeconds).IsRequired();
        b.Property(x => x.IsActive).IsRequired();

        // Mỗi view chỉ binding 1 lần.
        b.HasIndex(x => x.ViewName).IsUnique();
        // Tra theo (SourceSystem, RecordType) — đối chiếu với SourceProfile bên DataMatching.
        b.HasIndex(x => new { x.SourceSystem, x.RecordType });

        b.Ignore(x => x.DomainEvents);
    }
}
