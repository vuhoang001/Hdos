using Hdos.M01Service.Domain.Entities;
using Hdos.M01Service.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hdos.M01Service.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the M01 demo dataset matching the mock JSON used by the front-end.
/// Idempotent: only inserts when each table is empty so re-running the service
/// after a restart won't duplicate rows.
/// </summary>
public static class M01Seeder
{
    public static async Task SeedAsync(M01DbContext db, CancellationToken ct = default)
    {
        if (!await db.PhongKhams.AnyAsync(ct))
        {
            db.PhongKhams.AddRange(
                new PhongKham("NOI_TH-01", "Nội TH-01",   22, 18, MucDoTai.Vang),
                new PhongKham("NOI_TH-02", "Nội TH-02",   18, 12, MucDoTai.Xanh),
                new PhongKham("NOI_TH-03", "Nội TH-03",   45, 22, MucDoTai.Do),
                new PhongKham("NGOAI-01",  "Ngoại-01",    15, 10, MucDoTai.Xanh),
                new PhongKham("NGOAI-02",  "Ngoại-02",    28, 16, MucDoTai.Vang),
                new PhongKham("SAN-01",    "Sản-01",      12,  8, MucDoTai.Xanh),
                new PhongKham("NHI-01",    "Nhi-01",      19, 14, MucDoTai.Xanh),
                new PhongKham("TIM-01",    "Tim mạch-01", 35, 18, MucDoTai.Do));
        }

        if (!await db.BenhNhans.AnyAsync(ct))
        {
            db.BenhNhans.AddRange(
                new BenhNhan("BN001", "Nguyễn Văn An", Triage.P2, BenhNhanTrangThai.DangKham, "NOI_TH-01", "BS. Bình",  18),
                new BenhNhan("BN002", "Trần Thị B.",   Triage.P1, BenhNhanTrangThai.ChoKham,  "TIM-01",    null,         7),
                new BenhNhan("BN003", "Lê Văn C.",     Triage.P2, BenhNhanTrangThai.ChoCls,   "NGOAI-01",  "BS. Đức",   18),
                new BenhNhan("BN004", "Phạm Thị D.",   Triage.P2, BenhNhanTrangThai.ChoKham,  "NHI-01",    null,        11),
                new BenhNhan("BN005", "Hoàng Văn E.",  Triage.P3, BenhNhanTrangThai.DangKham, "SAN-01",    "BS. Giang",  5));
        }

        if (!await db.CapCuus.AnyAsync(ct))
        {
            db.CapCuus.AddRange(
                new CapCuuRecord("CU-001", "Nguyễn Văn An", Triage.P1,  7, null,        CapCuuTrangThai.ChuaPhanCong, true),
                new CapCuuRecord("CU-002", "Trần Thị B.",   Triage.P1,  3, "BS. Bình",  CapCuuTrangThai.DangXuLy,     false),
                new CapCuuRecord("CU-003", "Lê Văn C.",     Triage.P2, 18, "BS. Đức",   CapCuuTrangThai.DangXuLy,     true),
                new CapCuuRecord("CU-004", "Phạm Thị D.",   Triage.P2, 11, null,        CapCuuTrangThai.ChuaPhanCong, false),
                new CapCuuRecord("CU-005", "Hoàng Văn E.",  Triage.P3,  5, "BS. Giang", CapCuuTrangThai.DangXuLy,     false));
        }

        if (!await db.NhanSuTrucs.AnyAsync(ct))
        {
            db.NhanSuTrucs.AddRange(
                new NhanSuTruc("BS001", "BS. Nguyễn Văn A", "Nội",      "NOI_TH-01", 3, NhanSuTrangThai.DangKham),
                new NhanSuTruc("BS002", "BS. Bình",          "Tim mạch", "TIM-01",    5, NhanSuTrangThai.DangKham),
                new NhanSuTruc("BS003", "BS. Đức",           "Ngoại",    "NGOAI-01",  2, NhanSuTrangThai.DangKham),
                new NhanSuTruc("BS004", "BS. Giang",         "Sản",      "SAN-01",    1, NhanSuTrangThai.DangKham));
        }

        if (!await db.ForecastEntries.AnyAsync(ct))
        {
            db.ForecastEntries.AddRange(
                new ForecastEntry(1, "08:00",  49, 49),
                new ForecastEntry(2, "09:00",  52, 51),
                new ForecastEntry(3, "10:00",  72, null),
                new ForecastEntry(4, "11:00",  97, null),
                new ForecastEntry(5, "12:00", 144, null),
                new ForecastEntry(6, "13:00", 163, null),
                new ForecastEntry(7, "14:00", 138, null),
                new ForecastEntry(8, "15:00", 102, null));
        }

        if (!await db.ForecastMetas.AnyAsync(ct))
        {
            db.ForecastMetas.Add(new ForecastMeta("v2.1", "13:00", 4.2));
        }

        if (!await db.DashboardSnapshots.AnyAsync(ct))
        {
            db.DashboardSnapshots.Add(new DashboardSnapshot(
                tongLuotKham: 128, choKhamTbPhut: 18, choMaxPhut: 45,
                p1: 2, p2: 5, p3: 11, trongNguong: true,
                dangKy: 45, choKham: 12, dangKham: 8, choCls: 6,
                nhanKq: 4, keDonNv: 3, hoanThanh: 50, tatTbPhut: 22));
        }

        // Ensure KhoaDoanhThus table exists for databases previously created via EnsureCreated
        await db.Database.ExecuteSqlRawAsync(@"
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[KhoaDoanhThus]') AND type = N'U')
            BEGIN
                CREATE TABLE [dbo].[KhoaDoanhThus] (
                    [MaKhoa]      nvarchar(32)    NOT NULL,
                    [TenKhoa]     nvarchar(120)   NOT NULL,
                    [SoBenhNhan]  int             NOT NULL,
                    [TongThu]     decimal(18,2)   NOT NULL,
                    [BhytTra]     decimal(18,2)   NOT NULL,
                    [BnTra]       decimal(18,2)   NOT NULL,
                    [HaoPhiKhac]  decimal(18,2)   NOT NULL,
                    [NgayBaoCao]  datetime2       NOT NULL,
                    [CreatedAtUtc] datetime2      NOT NULL,
                    [UpdatedAtUtc] datetime2      NULL,
                    CONSTRAINT [PK_KhoaDoanhThus] PRIMARY KEY ([MaKhoa])
                );
                CREATE INDEX [IX_KhoaDoanhThus_NgayBaoCao] ON [dbo].[KhoaDoanhThus] ([NgayBaoCao]);
            END", ct);

        if (!await db.KhoaDoanhThus.AnyAsync(ct))
        {
            var ngay = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc);
            db.KhoaDoanhThus.AddRange(
                new KhoaDoanhThu("KH0001", "Khoa Nội - Hồi Sức Cấp Cứu", 1,  1_110_710m,  787_111m,    306_966m,  16_633m, ngay),
                new KhoaDoanhThu("PH0000", "Phòng khám Nội 1",           12, 10_330_308m, 1_109_857m, 9_196_538m,  23_913m, ngay),
                new KhoaDoanhThu("PH0001", "Phòng khám Ngoại",            1,    293_461m,   291_844m,     1_617m,       0m, ngay),
                new KhoaDoanhThu("PH0002", "Phòng khám Nhi",              1,     36_189m,    36_189m,         0m,       0m, ngay),
                new KhoaDoanhThu("PH0004", "Phòng khám Huyết áp Tiểu đường", 13, 5_095_476m, 4_790_725m, 304_751m,  0m, ngay),
                new KhoaDoanhThu("PH0005", "Phòng khám Sản",              4,  1_890_000m, 1_500_000m,   390_000m,       0m, ngay),
                new KhoaDoanhThu("PH0006", "Phòng khám Da liễu",          4,  1_173_835m,   812_328m,   361_507m,       0m, ngay),
                new KhoaDoanhThu("PH0007", "Phòng khám Nội 2",           20,  2_300_000m, 1_200_000m, 1_039_484m,  60_516m, ngay));
        }

        await db.SaveChangesAsync(ct);
    }
}
