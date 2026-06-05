namespace Hdos.DataMatchingService.Application.DTOs;

public sealed record SourceProfileDto(
    Guid Id,
    string SourceSystem,
    string RecordType,
    string DisplayName,
    string BusinessKeyField,
    Dictionary<string, string> Mappings);

// Schema Discovery — trả danh sách field FE có thể bind vào DataBinding expression.
// Chung contract với LakehouseService.SnapshotSchemaDto để FE xử lý cùng cách.
public sealed record DataSourceFieldDto(
    string  Key,
    string  Type,           // "string" | "number" | "date" | "boolean"
    string? Label,
    string? SourceField);   // tên field gốc trước khi rename qua mapping

public sealed record DataSourceSchemaDto(
    string                     Namespace,
    string                     BusinessKeyField,
    List<DataSourceFieldDto>   Fields);

public sealed record IngestResultDto(
    Guid Id,
    string SourceSystem,
    string RecordType,
    string? BusinessKey,
    string Status);

public sealed record IngestBatchResultDto(
    int Count,
    List<Guid> Ids);

// Dùng cho endpoint GET /dm/records — trả về canonical fields để client tự parse.
public sealed record StagingRecordDto(
    Guid Id,
    string SourceSystem,
    string RecordType,
    string? BusinessKey,
    string Status,
    string? CanonicalPayload,
    DateTime ReceivedAt,
    DateTime? ProcessedAt);

public sealed record ReportColumnDto(
    string Key,
    string Label,
    string Type);

public sealed record ReportRowDto(
    Dictionary<string, object?> Data);

public sealed record ReportDto(
    string ReportCode,
    string ReportName,
    DateTime GeneratedAt,
    List<ReportColumnDto> Columns,
    List<ReportRowDto> Rows,
    Dictionary<string, object?> Summary);

// --- M02: Trực quan khoa Nội ---

public sealed record M02SummaryDto(
    int TongGiuongDangSuDung,
    int TongGiuongKhaDung,
    double BorPercent,
    int DangDieuTri,
    int VaoVienHomNay,
    int RaVienHomNay,
    double Alos);

public sealed record M02DoiTuongDto(
    string DoiTuong,
    int SoLuong,
    double PhanTram);

public sealed record M02IcdDto(
    string MaIcd,
    string TenIcd,
    int SoLuong);

public sealed record M02BenhNhanDto(
    string Mrn,
    string TenBenhNhan,
    string TenKhoa,
    string? SoGiuong,
    string? NgayNhap,
    string? NgayXuat,
    string? DoiTuong,
    string? TrangThai,
    string? ChanDoan);

public sealed record WidgetCatalogDto(
    string ChartType,
    string Category,
    string Label,
    string Description,
    string Icon,
    List<string> RequiredColumns,
    List<string> OptionalColumns,
    List<string> CompatibleWith,
    int SortOrder);

public sealed record M02ReportDto(
    DateOnly ReportDate,
    DateTime GeneratedAt,
    M02SummaryDto Summary,
    List<M02DoiTuongDto> DoiTuongKcb,
    List<M02IcdDto> TopIcd,
    List<M02BenhNhanDto> BenhNhanNoiTru);
