using Hdos.LakehouseService.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.LakehouseService.Infrastructure.Persistence.Configurations;

public sealed class SyncStateConfiguration : IEntityTypeConfiguration<SyncState>
{
    public void Configure(EntityTypeBuilder<SyncState> b)
    {
        b.ToTable("WarehouseSyncStates");
        b.HasKey(x => x.ViewName);

        b.Property(x => x.ViewName).HasMaxLength(200).IsRequired();
        b.Property(x => x.LastSyncedAt).IsRequired();
        b.Property(x => x.LastRowCount).IsRequired();
        b.Property(x => x.LastJobId).HasMaxLength(200);
    }
}
