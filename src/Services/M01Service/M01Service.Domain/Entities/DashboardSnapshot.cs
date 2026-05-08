using Hdos.SharedKernel;

namespace Hdos.M01Service.Domain.Entities;

public sealed class DashboardSnapshot : BaseEntity<int>
{
    public int TongLuotKham { get; private set; }
    public int ChoKhamTbPhut { get; private set; }
    public int ChoMaxPhut { get; private set; }
    public int TriageP1 { get; private set; }
    public int TriageP2 { get; private set; }
    public int TriageP3 { get; private set; }
    public bool TrongNguong { get; private set; }

    public int FlowDangKy { get; private set; }
    public int FlowChoKham { get; private set; }
    public int FlowDangKham { get; private set; }
    public int FlowChoCls { get; private set; }
    public int FlowNhanKq { get; private set; }
    public int FlowKeDonNv { get; private set; }
    public int FlowHoanThanh { get; private set; }
    public int FlowTatTbPhut { get; private set; }

    private DashboardSnapshot() { }

    public DashboardSnapshot(int tongLuotKham, int choKhamTbPhut, int choMaxPhut,
        int p1, int p2, int p3, bool trongNguong,
        int dangKy, int choKham, int dangKham, int choCls, int nhanKq, int keDonNv,
        int hoanThanh, int tatTbPhut)
    {
        Id = 1;
        TongLuotKham = tongLuotKham;
        ChoKhamTbPhut = choKhamTbPhut;
        ChoMaxPhut = choMaxPhut;
        TriageP1 = p1;
        TriageP2 = p2;
        TriageP3 = p3;
        TrongNguong = trongNguong;

        FlowDangKy = dangKy;
        FlowChoKham = choKham;
        FlowDangKham = dangKham;
        FlowChoCls = choCls;
        FlowNhanKq = nhanKq;
        FlowKeDonNv = keDonNv;
        FlowHoanThanh = hoanThanh;
        FlowTatTbPhut = tatTbPhut;
    }
}
