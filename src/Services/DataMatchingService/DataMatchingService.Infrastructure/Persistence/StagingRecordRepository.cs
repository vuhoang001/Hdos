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

    public async Task<List<StagingRecord>> GetFilteredAsync(
        string? sourceSystem, string? recordType,
        string? field, string? value,
        DateTime? from, DateTime? to,
        int limit, CancellationToken ct)
    {
        // Lọc cứng theo sourceSystem/recordType/thời gian ở DB trước.
        var q = db.StagingRecords.Where(r => r.Status == RecordStatus.Matched);
        if (!string.IsNullOrEmpty(sourceSystem)) q = q.Where(r => r.SourceSystem == sourceSystem);
        if (!string.IsNullOrEmpty(recordType))   q = q.Where(r => r.RecordType   == recordType);
        if (from.HasValue) q = q.Where(r => r.ReceivedAt >= from.Value);
        if (to.HasValue)   q = q.Where(r => r.ReceivedAt <= to.Value);

        var candidates = await q.OrderByDescending(r => r.ReceivedAt)
                                .Take(limit * 10)   // lấy dư để bù cho filter in-memory phía dưới
                                .ToListAsync(ct);

        // Nếu không lọc theo field cụ thể thì trả về ngay.
        if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(value))
            return candidates.Take(limit).ToList();

        // Filter theo field trong CanonicalPayload — làm in-memory vì cột là text, không phải jsonb.
        return candidates
            .Where(r => MatchesField(r.CanonicalPayload, field, value))
            .Take(limit)
            .ToList();
    }

    // Kiểm tra xem CanonicalPayload có chứa field với value tương ứng không.
    // So sánh không phân biệt hoa thường để tìm kiếm dễ hơn.
    private static bool MatchesField(string? canonicalPayload, string field, string value)
    {
        if (string.IsNullOrEmpty(canonicalPayload)) return false;
        try
        {
            using var doc = JsonDocument.Parse(canonicalPayload);
            if (doc.RootElement.TryGetProperty(field, out var element))
                return element.ToString().Contains(value, StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return false;
    }
}
