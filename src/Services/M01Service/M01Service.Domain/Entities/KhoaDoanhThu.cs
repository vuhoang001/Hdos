using Hdos.SharedKernel;

namespace Hdos.M01Service.Domain.Entities;

public sealed class KhoaDoanhThu : BaseEntity<string>
{
    public string TenKhoa      { get; private set; } = default!;
    public int    SoBenhNhan   { get; private set; }
    public decimal TongThu     { get; private set; }
    public decimal BhytTra     { get; private set; }
    public decimal BnTra       { get; private set; }
    public decimal HaoPhiKhac  { get; private set; }
    public DateTime NgayBaoCao { get; private set; }

    private KhoaDoanhThu() { }

    public KhoaDoanhThu(
        string maKhoa, string tenKhoa,
        int soBenhNhan, decimal tongThu,
        decimal bhytTra, decimal bnTra, decimal haoPhiKhac,
        DateTime ngayBaoCao)
    {
        Id           = maKhoa;
        TenKhoa      = tenKhoa;
        SoBenhNhan   = soBenhNhan;
        TongThu      = tongThu;
        BhytTra      = bhytTra;
        BnTra        = bnTra;
        HaoPhiKhac   = haoPhiKhac;
        NgayBaoCao   = ngayBaoCao;
    }

    public void Update(
        string tenKhoa, int soBenhNhan,
        decimal tongThu, decimal bhytTra,
        decimal bnTra, decimal haoPhiKhac,
        DateTime ngayBaoCao)
    {
        TenKhoa      = tenKhoa;
        SoBenhNhan   = soBenhNhan;
        TongThu      = tongThu;
        BhytTra      = bhytTra;
        BnTra        = bnTra;
        HaoPhiKhac   = haoPhiKhac;
        NgayBaoCao   = ngayBaoCao;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
