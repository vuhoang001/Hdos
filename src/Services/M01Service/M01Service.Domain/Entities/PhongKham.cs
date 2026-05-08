using Hdos.M01Service.Domain.Enums;
using Hdos.SharedKernel;

namespace Hdos.M01Service.Domain.Entities;

public sealed class PhongKham : BaseEntity<string>
{
    public string TenPhong { get; private set; } = default!;
    public int ChoTbPhut { get; private set; }
    public int SoBenhNhan { get; private set; }
    public MucDoTai MucDoTai { get; private set; }

    private PhongKham() { }

    public PhongKham(string maPhong, string tenPhong, int choTbPhut, int soBenhNhan, MucDoTai mucDoTai)
    {
        Id = maPhong;
        TenPhong = tenPhong;
        ChoTbPhut = choTbPhut;
        SoBenhNhan = soBenhNhan;
        MucDoTai = mucDoTai;
    }
}
