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

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
