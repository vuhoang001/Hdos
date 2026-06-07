using Hdos.DataMatchingService.Domain.Entities;
using Hdos.SharedKernel;

namespace Hdos.DataMatchingService.Application.Services;

/// <summary>
/// Core ingest pipeline — dùng chung cho:
///   • <c>IngestJsonCommand</c> (REST POST /dm/ingest/json)
///   • <c>IngestFileCommand</c> (REST POST /dm/ingest/file, batch)
///   • <c>RawRecordIngestRequestedConsumer</c> (event từ LakehouseService / source providers khác)
///
/// Trách nhiệm: lookup <c>SourceProfile</c> → apply field mapping (raw → canonical) →
/// SHA-256 dedup → build <c>StagingRecord</c> chưa lưu. Caller chịu trách nhiệm save batch.
///
/// Xem doc 44 — Unified Ingest Pipeline §3.
/// </summary>
public interface IIngestCoreService
{
    /// <summary>
    /// Build 1 <c>StagingRecord</c> mới từ raw payload. KHÔNG save DB — caller tự gọi UoW.
    /// </summary>
    /// <returns>
    /// <c>Result.Failure(NotFound)</c> nếu chưa đăng ký SourceProfile cho cặp (sourceSystem, recordType).
    /// <c>Result.Success(null)</c> nếu payload trùng SHA-256 với record đã tồn tại — caller bỏ qua.
    /// <c>Result.Success(record)</c> nếu record mới — caller add vào repo + save.
    /// </returns>
    Task<Result<StagingRecord?>> TryBuildRecordAsync(
        string  sourceSystem,
        string  recordType,
        string  rawPayloadJson,
        string? businessKeyOverride,
        CancellationToken ct);
}
