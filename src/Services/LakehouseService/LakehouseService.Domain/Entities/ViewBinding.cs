using Hdos.SharedKernel;

namespace Hdos.LakehouseService.Domain.Entities;

/// <summary>
/// Đăng ký mapping 1 PostgreSQL view ngoài (warehouse) → 1 cặp (SourceSystem, RecordType)
/// trong DataMatchingService.
///
/// Mỗi binding xác định:
///   • View nào cần đọc (<see cref="ViewName"/>)
///   • Cột business key (<see cref="BusinessKeyColumn"/>) + cột mốc poll (<see cref="UpdatedAtColumn"/>)
///   • Đích publish event (<see cref="SourceSystem"/>, <see cref="RecordType"/>) —
///     DataMatching tra <c>SourceProfile</c> theo cặp này để canonicalize.
///
/// Xem doc 44 — Unified Ingest Pipeline §4.
/// </summary>
public sealed class ViewBinding : AggregateRoot<Guid>
{
    /// <summary>Schema-qualified VIEW name trong warehouse. VD: "warehouse.v_lab_results_v1".</summary>
    public string ViewName { get; private set; } = null!;

    /// <summary>Khóa thứ nhất tra SourceProfile. VD: "lakehouse:v_lab_results_v1".</summary>
    public string SourceSystem { get; private set; } = null!;

    /// <summary>Khóa thứ hai tra SourceProfile. VD: "lab-result".</summary>
    public string RecordType { get; private set; } = null!;

    /// <summary>Tên cột trong VIEW chứa business key. VD: "business_key".</summary>
    public string BusinessKeyColumn { get; private set; } = null!;

    /// <summary>Tên cột timestamp dùng để poll incremental. VD: "updated_at".</summary>
    public string UpdatedAtColumn { get; private set; } = null!;

    /// <summary>Khoảng cách giữa 2 lần poll, đơn vị giây. Tối thiểu 30s.</summary>
    public int PollIntervalSeconds { get; private set; }

    /// <summary>Binding có đang được worker pick up không.</summary>
    public bool IsActive { get; private set; }

    private ViewBinding() { }

    public static ViewBinding Create(
        string viewName,
        string sourceSystem,
        string recordType,
        string businessKeyColumn,
        string updatedAtColumn,
        int    pollIntervalSeconds)
    {
        return new ViewBinding
        {
            Id                  = Guid.NewGuid(),
            ViewName            = viewName,
            SourceSystem        = sourceSystem,
            RecordType          = recordType,
            BusinessKeyColumn   = businessKeyColumn,
            UpdatedAtColumn     = updatedAtColumn,
            PollIntervalSeconds = pollIntervalSeconds,
            IsActive            = true,
        };
    }

    public void Update(
        string viewName,
        string sourceSystem,
        string recordType,
        string businessKeyColumn,
        string updatedAtColumn,
        int    pollIntervalSeconds)
    {
        ViewName            = viewName;
        SourceSystem        = sourceSystem;
        RecordType          = recordType;
        BusinessKeyColumn   = businessKeyColumn;
        UpdatedAtColumn     = updatedAtColumn;
        PollIntervalSeconds = pollIntervalSeconds;
        UpdatedAtUtc        = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        if (IsActive == isActive) return;
        IsActive     = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
