namespace Hdos.M01Service.Application.DTOs;

public sealed record TriageBucketDto(int P1, int P2, int P3);

public sealed record DashboardSummaryDto(
    int TongLuotKham,
    int ChoKhamTbPhut,
    int ChoMaxPhut,
    TriageBucketDto Triage,
    bool TrongNguong,
    DateTime UpdatedAt);

public sealed record DashboardFlowDto(
    int DangKy,
    int ChoKham,
    int DangKham,
    int ChoCls,
    int NhanKq,
    int KeDonNv,
    int HoanThanh,
    int TatTbPhut,
    DateTime UpdatedAt);

public sealed record PhongKhamDto(
    string MaPhong,
    string TenPhong,
    int ChoTbPhut,
    int SoBenhNhan,
    string MucDoTai);

public sealed record BenhNhanDto(
    string MaBn,
    string HoTen,
    string Triage,
    string TrangThai,
    string PhongKham,
    string? BacSi,
    int ChoPhut);

public sealed record BenhNhanPageDto(int Total, int Page, IReadOnlyList<BenhNhanDto> Data);

public sealed record CapCuuDto(
    string MaCu,
    string HoTen,
    string Triage,
    int ChoPhut,
    string? BacSi,
    string TrangThai,
    bool CanhBao);

public sealed record CapCuuListDto(bool Live, int Total, IReadOnlyList<CapCuuDto> Data);

public sealed record NhanSuTrucDto(
    string Id,
    string HoTen,
    string Khoa,
    string PhongKham,
    int SoBnDangKham,
    string TrangThai);

public sealed record ForecastPointDto(string Gio, int DuBao, int? ThucTe);

public sealed record AiForecastDto(
    string ModelVersion,
    string CaoDiemDuKien,
    double DoChinhXacMae,
    IReadOnlyList<ForecastPointDto> DuBao);
