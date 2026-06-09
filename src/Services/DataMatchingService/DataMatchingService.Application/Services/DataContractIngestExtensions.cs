using System.Text.Json;
using Hdos.DataMatchingService.Domain.Entities;
using Hdos.SharedKernel;

namespace Hdos.DataMatchingService.Application.Services;

/// <summary>
/// Helper cho phép caller push typed contract row (record) vào ingest pipeline mà KHÔNG cần
/// tự serialize. Chuyển 1 typed row qua <see cref="JsonSerializer"/> → gọi
/// <see cref="IIngestCoreService.TryBuildRecordAsync"/> với contractCode làm recordType
/// và sourceSystem mà caller chỉ định.
///
/// Caller VẪN phải đăng ký <c>SourceProfile</c> cho cặp (sourceSystem, contractCode)
/// để mapping hoạt động (xem doc 44 §3, doc 45). Nếu profile có FieldMappingsJson rỗng,
/// canonical payload = raw payload (passthrough).
/// </summary>
public static class DataContractIngestExtensions
{
    public static Task<Result<StagingRecord?>> IngestContractRowAsync<TSchema>(
        this IIngestCoreService core,
        string contractCode,
        TSchema row,
        string sourceSystem,
        string? businessKeyOverride = null,
        CancellationToken ct = default) where TSchema : class
    {
        ArgumentException.ThrowIfNullOrEmpty(contractCode);
        ArgumentException.ThrowIfNullOrEmpty(sourceSystem);
        ArgumentNullException.ThrowIfNull(row);

        var payload = JsonSerializer.Serialize(row);
        return core.TryBuildRecordAsync(
            sourceSystem:        sourceSystem,
            recordType:          contractCode,
            rawPayloadJson:      payload,
            businessKeyOverride: businessKeyOverride,
            ct:                  ct);
    }
}
