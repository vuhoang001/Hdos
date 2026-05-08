using Hdos.M01Service.Domain.Enums;
using Hdos.SharedKernel;

namespace Hdos.M01Service.Domain.Entities;

public sealed class BenhNhan : BaseEntity<string>
{
    public string HoTen { get; private set; } = default!;
    public Triage Triage { get; private set; }
    public BenhNhanTrangThai TrangThai { get; private set; }
    public string MaPhongKham { get; private set; } = default!;
    public string? BacSi { get; private set; }
    public int ChoPhut { get; private set; }

    private BenhNhan() { }

    public BenhNhan(string maBn, string hoTen, Triage triage, BenhNhanTrangThai trangThai,
        string maPhongKham, string? bacSi, int choPhut)
    {
        Id = maBn;
        HoTen = hoTen;
        Triage = triage;
        TrangThai = trangThai;
        MaPhongKham = maPhongKham;
        BacSi = bacSi;
        ChoPhut = choPhut;
    }
}
