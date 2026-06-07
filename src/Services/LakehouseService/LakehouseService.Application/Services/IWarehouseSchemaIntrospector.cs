namespace Hdos.LakehouseService.Application.Services;

/// <summary>
/// Đọc metadata schema của VIEW từ warehouse external (Postgres) qua
/// <c>information_schema.columns</c>. Implementation ở Infrastructure
/// vì cần <c>NpgsqlDataSource</c>.
///
/// Dùng cho 2 luồng (xem doc 45):
///   • <c>CreateWithAutoProfile</c> — auto sinh mappings (MVP B)
///   • <c>PreviewSchema</c> — gợi ý cho admin chỉnh (hướng C)
/// </summary>
public interface IWarehouseSchemaIntrospector
{
    /// <summary>
    /// Trả danh sách column theo thứ tự (ordinal_position).
    /// Mảng rỗng nếu view không tồn tại hoặc <c>hdos_reader</c> thiếu quyền SELECT.
    /// </summary>
    /// <param name="viewName">Schema-qualified, vd <c>warehouse.v_lab_results_v1</c></param>
    Task<List<ColumnMetadata>> GetColumnsAsync(string viewName, CancellationToken ct);
}

public sealed record ColumnMetadata(
    string Name,
    string DataType,
    bool   Nullable);
