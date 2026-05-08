using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Domain.Entities;
using Hdos.M01Service.Domain.Enums;

namespace Hdos.M01Service.Application.Mapping;

internal static class M01Mapping
{
    public static string ToWire(this Triage t) => t switch
    {
        Triage.P1 => "P1",
        Triage.P2 => "P2",
        Triage.P3 => "P3",
        _ => t.ToString()
    };

    public static string ToWire(this MucDoTai m) => m switch
    {
        MucDoTai.Xanh => "xanh",
        MucDoTai.Vang => "vang",
        MucDoTai.Do => "do",
        _ => m.ToString().ToLowerInvariant()
    };

    public static string ToWire(this BenhNhanTrangThai s) => s switch
    {
        BenhNhanTrangThai.DangKy => "dang_ky",
        BenhNhanTrangThai.ChoKham => "cho_kham",
        BenhNhanTrangThai.DangKham => "dang_kham",
        BenhNhanTrangThai.ChoCls => "cho_cls",
        BenhNhanTrangThai.NhanKq => "nhan_kq",
        BenhNhanTrangThai.KeDonNv => "ke_don_nv",
        BenhNhanTrangThai.HoanThanh => "hoan_thanh",
        _ => s.ToString().ToLowerInvariant()
    };

    public static string ToWire(this CapCuuTrangThai s) => s switch
    {
        CapCuuTrangThai.ChuaPhanCong => "chua_phan_cong",
        CapCuuTrangThai.DangXuLy => "dang_xu_ly",
        CapCuuTrangThai.HoanThanh => "hoan_thanh",
        _ => s.ToString().ToLowerInvariant()
    };

    public static string ToWire(this NhanSuTrangThai s) => s switch
    {
        NhanSuTrangThai.NghiTruc => "nghi_truc",
        NhanSuTrangThai.DangKham => "dang_kham",
        NhanSuTrangThai.Ban => "ban",
        _ => s.ToString().ToLowerInvariant()
    };

    public static PhongKhamDto ToDto(this PhongKham p) =>
        new(p.Id, p.TenPhong, p.ChoTbPhut, p.SoBenhNhan, p.MucDoTai.ToWire());

    public static BenhNhanDto ToDto(this BenhNhan b) =>
        new(b.Id, b.HoTen, b.Triage.ToWire(), b.TrangThai.ToWire(), b.MaPhongKham, b.BacSi, b.ChoPhut);

    public static CapCuuDto ToDto(this CapCuuRecord c) =>
        new(c.Id, c.HoTen, c.Triage.ToWire(), c.ChoPhut, c.BacSi, c.TrangThai.ToWire(), c.CanhBao);

    public static NhanSuTrucDto ToDto(this NhanSuTruc n) =>
        new(n.Id, n.HoTen, n.Khoa, n.MaPhongKham, n.SoBnDangKham, n.TrangThai.ToWire());

    public static ForecastPointDto ToDto(this ForecastEntry f) =>
        new(f.Gio, f.DuBao, f.ThucTe);
}
