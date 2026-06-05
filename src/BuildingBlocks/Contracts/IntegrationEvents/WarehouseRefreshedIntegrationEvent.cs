namespace Hdos.Contracts.IntegrationEvents;

/// <summary>
/// Phát ra bởi pipeline Data Engineering khi refresh xong một (hoặc nhiều) VIEW trong warehouse.
/// LakehouseService consume → tự động chạy <c>WarehouseViewSyncer</c> để pull data về snapshot.
///
/// Use case: DE muốn báo Hdos "data mới sẵn rồi" mà không cần Hdos poll theo lịch.
/// </summary>
/// <param name="ViewNames">
/// Danh sách tên VIEW (không kèm schema) cần sync.
/// VD: <c>["encounter_activity_daily", "finance_daily"]</c>.
/// Nếu mảng rỗng → consumer sync TẤT CẢ view đã hỗ trợ.
/// </param>
/// <param name="RefreshedAt">Thời điểm DE pipeline hoàn tất refresh.</param>
/// <param name="RequestedBy">
/// (Optional) Ai/cái gì trigger refresh. VD: <c>"airflow-dag-daily-001"</c>, <c>"manual-replay"</c>.
/// Dùng cho audit log.
/// </param>
public sealed record WarehouseRefreshedIntegrationEvent(
    string[] ViewNames,
    DateTime RefreshedAt,
    string? RequestedBy) : IntegrationEvent;
