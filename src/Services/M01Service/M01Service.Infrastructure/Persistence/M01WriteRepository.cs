using Hdos.M01Service.Domain.Entities;
using Hdos.M01Service.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.M01Service.Infrastructure.Persistence;

public sealed class M01WriteRepository(M01DbContext db) : IM01WriteRepository
{
    public Task<KhoaDoanhThu?> FindKhoaDoanhThuAsync(string maKhoa, CancellationToken ct) =>
        db.KhoaDoanhThus.FindAsync([maKhoa], ct).AsTask();

    public async Task UpsertKhoaDoanhThuAsync(KhoaDoanhThu entity, CancellationToken ct)
    {
        var existing = await db.KhoaDoanhThus.FindAsync([entity.Id], ct);
        if (existing is null)
            db.KhoaDoanhThus.Add(entity);
    }

    public Task<decimal> GetTongDoanhThuNgayAsync(DateTime ngayBaoCao, CancellationToken ct) =>
        db.KhoaDoanhThus
            .Where(x => x.NgayBaoCao.Date == ngayBaoCao.Date)
            .SumAsync(x => x.TongThu, ct);

    public Task<int> GetTongLuotKhamNgayAsync(DateTime ngayBaoCao, CancellationToken ct) =>
        db.KhoaDoanhThus
            .Where(x => x.NgayBaoCao.Date == ngayBaoCao.Date)
            .SumAsync(x => x.SoBenhNhan, ct);

    public async Task<(int TongLuotKham, decimal TongDoanhThu, decimal DoanhThuTrungBinhTheoTuan)> GetAllTimeTotalsAsync(CancellationToken ct)
    {
        var all         = await db.KhoaDoanhThus.ToListAsync(ct);
        var tongLuot    = all.Sum(x => x.SoBenhNhan);
        var tongThu     = all.Sum(x => x.TongThu);
        var minNgay     = all.Min(x => x.NgayBaoCao);
        var maxNgay     = all.Max(x => x.NgayBaoCao);
        var soTuan      = (decimal)Math.Max(1, Math.Ceiling((maxNgay - minNgay).TotalDays / 7.0));
        var tbTuan      = Math.Round(tongThu / soTuan, 0);
        return (tongLuot, tongThu, tbTuan);
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
