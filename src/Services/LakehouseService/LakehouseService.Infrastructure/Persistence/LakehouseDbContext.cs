using Hdos.LakehouseService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hdos.LakehouseService.Infrastructure.Persistence;

public sealed class LakehouseDbContext(DbContextOptions<LakehouseDbContext> options) : DbContext(options)
{
    public DbSet<LakehouseSnapshot> Snapshots => Set<LakehouseSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LakehouseDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
