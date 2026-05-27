using Hdos.DataMatchingService.Domain.Entities;

namespace Hdos.DataMatchingService.Domain.Repositories;

public interface IStagingRecordRepository
{
    Task AddAsync(StagingRecord record, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<StagingRecord> records, CancellationToken ct);
    Task<StagingRecord?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<StagingRecord>> GetPendingBatchAsync(int batchSize, CancellationToken ct);
    Task<bool> ExistsHashAsync(string hash, CancellationToken ct);

    // Dùng bởi báo cáo aggregate — chỉ lấy record Matched, filter theo nguồn/loại/thời gian.
    Task<List<StagingRecord>> GetMatchedAsync(
        string? sourceSystem,
        string? recordType,
        DateTime? from,
        DateTime? to,
        CancellationToken ct);

    // Dùng bởi endpoint search — filter linh hoạt theo bất kỳ field nào trong CanonicalPayload.
    // field/value được filter in-memory sau khi load từ DB vì CanonicalPayload là text, không phải jsonb.
    Task<List<StagingRecord>> GetFilteredAsync(
        string? sourceSystem,
        string? recordType,
        string? field,
        string? value,
        DateTime? from,
        DateTime? to,
        int limit,
        CancellationToken ct);
}
