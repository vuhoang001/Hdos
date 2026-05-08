using Hdos.M01Service.Domain.Enums;
using Hdos.SharedKernel;

namespace Hdos.M01Service.Domain.Entities;

public sealed class NhanSuTruc : BaseEntity<string>
{
    public string HoTen { get; private set; } = default!;
    public string Khoa { get; private set; } = default!;
    public string MaPhongKham { get; private set; } = default!;
    public int SoBnDangKham { get; private set; }
    public NhanSuTrangThai TrangThai { get; private set; }

    private NhanSuTruc() { }

    public NhanSuTruc(string id, string hoTen, string khoa, string maPhongKham,
        int soBnDangKham, NhanSuTrangThai trangThai)
    {
        Id = id;
        HoTen = hoTen;
        Khoa = khoa;
        MaPhongKham = maPhongKham;
        SoBnDangKham = soBnDangKham;
        TrangThai = trangThai;
    }
}
