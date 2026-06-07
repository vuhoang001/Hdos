namespace Hdos.LakehouseService.Application.Helpers;

/// <summary>
/// Sinh canonical field name từ raw column name của warehouse view.
/// Dùng chung cho 2 luồng:
///   • <c>CreateWithAutoProfile</c> — sinh mappings hoàn toàn tự động (MVP B)
///   • <c>PreviewSchema</c> — gợi ý canonical, admin chỉnh tay (hướng C)
///
/// Quy tắc (xem doc 45 §6.4.2):
///   1. Healthcare domain overrides — biết trước vd <c>business_key</c> → <c>MaBenhNhan</c>
///   2. Timestamp <c>created_at</c>/<c>updated_at</c> → prefix <c>_</c> (FE bỏ qua trong render form)
///   3. Fallback: snake_case → PascalCase
/// </summary>
public static class CanonicalNameSuggester
{
    public static string Suggest(string rawColumnName)
    {
        var lower = rawColumnName.ToLowerInvariant();
        return lower switch
        {
            // Business key — patient identifier
            "business_key" or "patient_id" or "ma_benh_nhan" or "ma_bn"
                => "MaBenhNhan",

            // Patient name
            "patient_name" or "ho_ten" or "full_name" or "ten_benh_nhan"
                => "TenBenhNhan",

            // Date of birth
            "date_of_birth" or "ngay_sinh" or "dob" or "birthday"
                => "NgaySinh",

            // Department
            "department" or "khoa" or "khoa_dieu_tri" or "department_name"
                => "KhoaDieuTri",

            // Diagnosis
            "diagnosis" or "chan_doan" or "icd10" or "diagnosis_name"
                => "ChanDoan",

            // Admission date
            "admission_date" or "ngay_nhap_vien" or "admit_date" or "encounter_date"
                => "NgayNhapVien",

            // Audit timestamps — prefix `_` để FE biết bỏ qua trong render form
            "created_at"     => "_created_at",
            "updated_at"     => "_updated_at",
            "received_at"    => "_received_at",
            "processed_at"   => "_processed_at",

            // Fallback: snake_case → PascalCase
            _ => SnakeToPascal(rawColumnName)
        };
    }

    public static bool IsBusinessKeyCandidate(string columnName, bool nullable) =>
        !nullable && (
            columnName.Equals("business_key",   StringComparison.OrdinalIgnoreCase) ||
            columnName.Equals("patient_id",     StringComparison.OrdinalIgnoreCase) ||
            columnName.Equals("ma_benh_nhan",   StringComparison.OrdinalIgnoreCase) ||
            columnName.Equals("ma_bn",          StringComparison.OrdinalIgnoreCase) ||
            columnName.EndsWith("_id",          StringComparison.OrdinalIgnoreCase) ||
            columnName.EndsWith("_key",         StringComparison.OrdinalIgnoreCase));

    public static bool IsUpdatedAtCandidate(string columnName, string dataType, bool nullable) =>
        !nullable &&
        (dataType.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase) || dataType == "date") &&
        (columnName.EndsWith("_at",   StringComparison.OrdinalIgnoreCase) ||
         columnName.EndsWith("_time", StringComparison.OrdinalIgnoreCase));

    private static string SnakeToPascal(string snake) =>
        string.Concat(snake.Split('_')
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpper(p[0]) + p[1..].ToLowerInvariant()));
}
