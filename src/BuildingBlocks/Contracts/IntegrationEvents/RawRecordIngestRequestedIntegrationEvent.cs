namespace Hdos.Contracts.IntegrationEvents;

/// <summary>
/// Yêu cầu ingest 1 record raw vào pipeline canonical của DataMatchingService.
///
/// Producer: bất kỳ service nào đóng vai "Source Provider" (LakehouseService poll PG view,
/// connector Excel/CSV, gateway nhận API ngoài, ...). Mỗi event đại diện cho 1 record duy nhất
/// — producer phải fan-out nếu có batch.
///
/// Consumer: DataMatchingService — lookup <c>SourceProfile</c> theo (SourceSystem, RecordType),
/// apply field mapping → canonical payload, SHA-256 dedup, lưu <c>StagingRecord</c>.
///
/// Hợp đồng và kiến trúc tổng thể: xem docs/44-unified-ingest-pipeline.md.
/// </summary>
/// <param name="SourceSystem">
/// Khóa thứ nhất tra <c>SourceProfile</c>. Convention: nguồn lakehouse dùng tiền tố
/// <c>"lakehouse:"</c> (VD <c>"lakehouse:v_lab_results_v1"</c>) để phân biệt với HIS/BHYT.
/// </param>
/// <param name="RecordType">
/// Khóa thứ hai tra <c>SourceProfile</c>. VD <c>"lab-result"</c>, <c>"benh-nhan"</c>.
/// </param>
/// <param name="BusinessKey">
/// Khóa nghiệp vụ. Producer set sẵn để consumer khỏi parse JSON.
/// Nếu rỗng, consumer fallback dùng <c>SourceProfile.BusinessKeyField</c> để trích từ payload.
/// </param>
/// <param name="RawPayloadJson">
/// JSON string của 1 record raw (chưa qua mapping). Object JSON, không phải array.
/// </param>
/// <param name="SourceJobId">
/// (Optional) ID của batch/poll job sinh ra event. VD <c>"sync-lab-result-20260605103000"</c>.
/// Dùng để truy vết theo batch khi debug.
/// </param>
public sealed record RawRecordIngestRequestedIntegrationEvent(
    string  SourceSystem,
    string  RecordType,
    string  BusinessKey,
    string  RawPayloadJson,
    string? SourceJobId) : IntegrationEvent;
