using Hdos.M01Service.Domain.Entities;
using Hdos.M01Service.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.M01Service.Infrastructure.Persistence;

public sealed class M01ReadRepository : IM01ReadRepository
{
    private readonly M01DbContext _db;

    public M01ReadRepository(M01DbContext db) => _db = db;

    public Task<DashboardSnapshot?> GetDashboardSnapshotAsync(CancellationToken ct) =>
        _db.DashboardSnapshots.AsNoTracking().FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PhongKham>> ListPhongKhamAsync(CancellationToken ct) =>
        await _db.PhongKhams.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);

    public async Task<(IReadOnlyList<BenhNhan> Items, int Total)> ListBenhNhansAsync(
        int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var query = _db.BenhNhans.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Triage)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<CapCuuRecord>> ListCapCuusAsync(CancellationToken ct) =>
        await _db.CapCuus.AsNoTracking()
            .OrderBy(x => x.Triage)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<NhanSuTruc>> ListNhanSuTrucAsync(CancellationToken ct) =>
        await _db.NhanSuTrucs.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);

    public async Task<(ForecastMeta? Meta, IReadOnlyList<ForecastEntry> Entries)> GetForecastAsync(
        CancellationToken ct)
    {
        var meta = await _db.ForecastMetas.AsNoTracking().FirstOrDefaultAsync(ct);
        var entries = await _db.ForecastEntries.AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        return (meta, entries);
    }

    public async Task<IReadOnlyList<KhoaDoanhThu>> ListKhoaDoanhThuAsync(
        DateTime? ngayBaoCao, CancellationToken ct)
    {
        var q = _db.KhoaDoanhThus.AsNoTracking();
        if (ngayBaoCao.HasValue)
            q = q.Where(x => x.NgayBaoCao.Date == ngayBaoCao.Value.Date);
        return await q.OrderBy(x => x.Id).ToListAsync(ct);
    }

    public Task<KhoaDoanhThu?> GetKhoaDoanhThuAsync(string maKhoa, CancellationToken ct) =>
        _db.KhoaDoanhThus.AsNoTracking().FirstOrDefaultAsync(x => x.Id == maKhoa, ct);
}
