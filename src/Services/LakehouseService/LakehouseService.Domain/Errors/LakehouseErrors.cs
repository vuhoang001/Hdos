using Hdos.SharedKernel;

namespace Hdos.LakehouseService.Domain.Errors;

public static class LakehouseErrors
{
    public static readonly Error SnapshotNotFound =
        new("Lakehouse.SnapshotNotFound", "Snapshot không tìm thấy.");

    public static readonly Error PayloadMissing =
        new("Lakehouse.PayloadMissing", "Event không có payload lẫn downloadUrl.");

    public static readonly Error DownloadFailed =
        new("Lakehouse.DownloadFailed", "Không thể tải dữ liệu từ downloadUrl.");
}
