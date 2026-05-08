using Hdos.M01Service.Domain.Enums;
using Hdos.SharedKernel;

namespace Hdos.M01Service.Domain.Entities;

public sealed class CapCuuRecord : BaseEntity<string>
{
    public string HoTen { get; private set; } = default!;
    public Triage Triage { get; private set; }
    public int ChoPhut { get; private set; }
    public string? BacSi { get; private set; }
    public CapCuuTrangThai TrangThai { get; private set; }
    public bool CanhBao { get; private set; }

    private CapCuuRecord() { }

    public CapCuuRecord(string maCu, string hoTen, Triage triage, int choPhut,
        string? bacSi, CapCuuTrangThai trangThai, bool canhBao)
    {
        Id = maCu;
        HoTen = hoTen;
        Triage = triage;
        ChoPhut = choPhut;
        BacSi = bacSi;
        TrangThai = trangThai;
        CanhBao = canhBao;
    }
}
