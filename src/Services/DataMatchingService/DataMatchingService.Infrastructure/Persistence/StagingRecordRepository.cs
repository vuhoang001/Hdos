using System.Text.Json;
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
        string? sourceSystem, string? recordType, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = db.StagingRecords.Where(r => r.Status == RecordStatus.Matched);
        if (!string.IsNullOrEmpty(sourceSystem)) q = q.Where(r => r.SourceSystem == sourceSystem);
        if (!string.IsNullOrEmpty(recordType))   q = q.Where(r => r.RecordType   == recordType);
        if (from.HasValue) q = q.Where(r => r.ReceivedAt >= from.Value);
        if (to.HasValue)   q = q.Where(r => r.ReceivedAt <= to.Value);
        return q.OrderBy(r => r.ReceivedAt).ToListAsync(ct);
    }

    public Task<List<StagingRecord>> GetFilteredAsync(
        string? sourceSystem, string? recordType,
        string? field, string? value,
        DateTime? from, DateTime? to,
        int limit, CancellationToken ct)
    {
        var q = db.StagingRecords.Where(r => r.Status == RecordStatus.Matched);
        if (!string.IsNullOrEmpty(sourceSystem)) q = q.Where(r => r.SourceSystem == sourceSystem);
        if (!string.IsNullOrEmpty(recordType))   q = q.Where(r => r.RecordType   == recordType);
        if (from.HasValue) q = q.Where(r => r.ReceivedAt >= from.Value);
        if (to.HasValue)   q = q.Where(r => r.ReceivedAt <= to.Value);

        if (!string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(value))
        {
            // Tạo JSON containment filter: {"TenKhoa": "Tim Mach"}
            // Toán tử @> kiểm tra CanonicalPayload có chứa cặp key-value này không.
            // PostgreSQL dùng GIN index (jsonb_path_ops) → O(log n) thay vì O(n).
            // Lưu ý: đây là exact match, không phải contains.
            // "Tim Mach" tìm được, "tim mach" không tìm được (jsonb @> case-sensitive).
            var jsonFilter = JsonSerializer.Serialize(
                new Dictionary<string, string> { [field] = value });

            q = q.Where(r => EF.Functions.JsonContains(r.CanonicalPayload!, jsonFilter));
        }

        return q
            .OrderByDescending(r => r.ReceivedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
