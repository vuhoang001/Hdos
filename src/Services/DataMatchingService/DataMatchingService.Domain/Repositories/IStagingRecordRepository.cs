using Hdos.DataMatchingService.Domain.Entities;

namespace Hdos.DataMatchingService.Domain.Repositories;

public interface IStagingRecordRepository
{
    Task AddAsync(StagingRecord record, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<StagingRecord> records, CancellationToken ct);
    Task<StagingRecord?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<StagingRecord>> GetPendingBatchAsync(int batchSize, CancellationToken ct);
    Task<bool> ExistsHashAsync(string hash, CancellationToken ct);
    Task<List<StagingRecord>> GetMatchedAsync(string? sourceSystem, DateTime? from, DateTime? to, CancellationToken ct);
}
