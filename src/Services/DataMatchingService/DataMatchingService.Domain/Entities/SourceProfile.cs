using System.Text.Json;
using Hdos.SharedKernel;

namespace Hdos.DataMatchingService.Domain.Entities;

// SourceProfile = bản khai báo cho một loại tài liệu từ một nguồn cụ thể.
// Khóa duy nhất là (SourceSystem, RecordType) — cùng nguồn nhưng khác loại tài liệu
// thì dùng mapping riêng. Ví dụ: his-01/benh-nhan khác his-01/chung-tu.
public sealed class SourceProfile : BaseEntity<Guid>
{
    // Mã nguồn dữ liệu. Ví dụ: "his-01", "bhyt-hn".
    public string SourceSystem { get; private set; } = null!;

    // Loại tài liệu trong nguồn đó. Ví dụ: "benh-nhan", "chung-tu", "don-thuoc".
    // Cùng với SourceSystem tạo thành khóa duy nhất.
    public string RecordType { get; private set; } = null!;

    // Tên hiển thị cho người dùng. Ví dụ: "HIS BV A — Bệnh nhân".
    public string DisplayName { get; private set; } = null!;

    // Tên field trong CanonicalPayload dùng làm khóa nghiệp vụ.
    // Phải là một trong các canonical value của mappings.
    public string BusinessKeyField { get; private set; } = null!;

    // Bảng ánh xạ tên field: { "tên_gốc": "tên_chuẩn" }.
    // Lưu JSON string để tránh bảng phụ, deserialize qua GetMappings() khi dùng.
    public string FieldMappingsJson { get; private set; } = null!;

    private SourceProfile() { }

    public static SourceProfile Create(
        string sourceSystem,
        string recordType,
        string displayName,
        string businessKeyField,
        Dictionary<string, string> mappings)
    {
        return new SourceProfile
        {
            Id                = Guid.NewGuid(),
            SourceSystem      = sourceSystem,
            RecordType        = recordType,
            DisplayName       = displayName,
            BusinessKeyField  = businessKeyField,
            FieldMappingsJson = JsonSerializer.Serialize(mappings)
        };
    }

    public void Update(
        string displayName,
        string businessKeyField,
        Dictionary<string, string> mappings)
    {
        DisplayName       = displayName;
        BusinessKeyField  = businessKeyField;
        FieldMappingsJson = JsonSerializer.Serialize(mappings);
        UpdatedAtUtc      = DateTime.UtcNow;
    }

    public Dictionary<string, string> GetMappings() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(FieldMappingsJson)
        ?? new Dictionary<string, string>();
}
