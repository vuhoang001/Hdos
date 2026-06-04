using Hdos.LakehouseService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.LakehouseService.Infrastructure.Persistence.Configurations;

public sealed class LakehouseSnapshotConfiguration : IEntityTypeConfiguration<LakehouseSnapshot>
{
    public void Configure(EntityTypeBuilder<LakehouseSnapshot> b)
    {
        b.ToTable("LakehouseSnapshots");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Namespace).HasMaxLength(200).IsRequired();
        b.Property(x => x.BusinessKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.JobId).HasMaxLength(200).IsRequired();
        // jsonb: PostgreSQL index + filter bằng toán tử @> trực tiếp trong SQL
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ReceivedAt).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.Namespace, x.BusinessKey });
        b.HasIndex(x => x.ReceivedAt);
        b.HasIndex(x => x.JobId);
    }
}
