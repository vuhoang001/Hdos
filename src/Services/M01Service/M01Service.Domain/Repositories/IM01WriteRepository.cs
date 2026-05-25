using Hdos.M01Service.Domain.Entities;

namespace Hdos.M01Service.Domain.Repositories;

public interface IM01WriteRepository
{
    Task<KhoaDoanhThu?> FindKhoaDoanhThuAsync(string maKhoa, CancellationToken ct);
    Task UpsertKhoaDoanhThuAsync(KhoaDoanhThu entity, CancellationToken ct);
    Task<decimal> GetTongDoanhThuNgayAsync(DateTime ngayBaoCao, CancellationToken ct);
    Task<int> GetTongLuotKhamNgayAsync(DateTime ngayBaoCao, CancellationToken ct);
    Task<(int TongLuotKham, decimal TongDoanhThu, decimal DoanhThuTrungBinhTheoTuan)> GetAllTimeTotalsAsync(CancellationToken ct);
}
