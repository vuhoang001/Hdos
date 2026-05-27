using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Enums;
using Hdos.DataMatchingService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DataMatchingService.Infrastructure.Persistence;

public sealed class StagingRecordRepository(DataMatchingDbContext db) : IStagingRecordRepository
{
    public async Task AddAsync(StagingRecord record, CancellationToken ct) =>
        await db.StagingRecords.AddAsync(record, ct);

    public async Task AddRangeAsync(IEnumerable<StagingRecord> records, CancellationToken ct) =>
        await db.StagingRecords.AddRangeAsync(records, ct);

    public Task<StagingRecord?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.StagingRecords.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<StagingRecord>> GetPendingBatchAsync(int batchSize, CancellationToken ct) =>
        db.StagingRecords
            .Where(r => r.Status == RecordStatus.Pending)
            .OrderBy(r => r.ReceivedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public Task<bool> ExistsHashAsync(string hash, CancellationToken ct) =>
        db.StagingRecords.AnyAsync(r => r.PayloadHash == hash, ct);

    public Task<List<StagingRecord>> GetMatchedAsync(
        string? sourceSystem,
        DateTime? from,
        DateTime? to,
        CancellationToken ct)
    {
        var query = db.StagingRecords
            .Where(r => r.Status == RecordStatus.Matched);

        if (!string.IsNullOrEmpty(sourceSystem))
            query = query.Where(r => r.SourceSystem == sourceSystem);

        if (from.HasValue)
            query = query.Where(r => r.ReceivedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(r => r.ReceivedAt <= to.Value);

        return query.OrderBy(r => r.ReceivedAt).ToListAsync(ct);
    }
}
