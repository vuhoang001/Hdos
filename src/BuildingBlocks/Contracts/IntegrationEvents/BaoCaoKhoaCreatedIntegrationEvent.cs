namespace Hdos.Contracts.IntegrationEvents;

public sealed record BaoCaoKhoaCreatedIntegrationEvent(
    string   MaKhoa,
    string   TenKhoa,
    int      SoBenhNhan,
    decimal  TongThu,
    DateTime NgayBaoCao,
    decimal  TongDoanhThuNgay)
    : IntegrationEvent;
