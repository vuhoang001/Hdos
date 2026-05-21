using Hdos.M01Service.Domain.Entities;

namespace Hdos.M01Service.Domain.Repositories;

public interface IM01ReadRepository
{
    Task<DashboardSnapshot?> GetDashboardSnapshotAsync(CancellationToken ct);
    Task<IReadOnlyList<PhongKham>> ListPhongKhamAsync(CancellationToken ct);
    Task<(IReadOnlyList<BenhNhan> Items, int Total)> ListBenhNhansAsync(int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<CapCuuRecord>> ListCapCuusAsync(CancellationToken ct);
    Task<IReadOnlyList<NhanSuTruc>> ListNhanSuTrucAsync(CancellationToken ct);
    Task<(ForecastMeta? Meta, IReadOnlyList<ForecastEntry> Entries)> GetForecastAsync(CancellationToken ct);
    Task<IReadOnlyList<KhoaDoanhThu>> ListKhoaDoanhThuAsync(DateTime? ngayBaoCao, CancellationToken ct);
    Task<KhoaDoanhThu?> GetKhoaDoanhThuAsync(string maKhoa, CancellationToken ct);
}
