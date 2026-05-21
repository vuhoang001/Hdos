namespace Hdos.Contracts.IntegrationEvents;

public sealed record BaoCaoKhoaCreatedIntegrationEvent(
    int     TongLuotKham,
    decimal TongDoanhThu,
    decimal DoanhThuTrungBinhTheoTuan,
    DateTime NgayBaoCao)
    : IntegrationEvent;
