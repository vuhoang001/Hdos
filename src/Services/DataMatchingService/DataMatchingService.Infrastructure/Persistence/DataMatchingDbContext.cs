using Hdos.DataMatchingService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DataMatchingService.Infrastructure.Persistence;

public sealed class DataMatchingDbContext(DbContextOptions<DataMatchingDbContext> options) : DbContext(options)
{
    public DbSet<StagingRecord> StagingRecords => Set<StagingRecord>();
    public DbSet<SourceProfile> SourceProfiles => Set<SourceProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataMatchingDbContext).Assembly);

        // Outbox + Inbox tables for MassTransit EF Outbox
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        base.OnModelCreating(modelBuilder);
    }
}
