using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.M01Service.Application.Features.BaoCao;

public sealed record GetBaoCaoKhoaQuery(DateTime? NgayBaoCao) : IRequest<Result<BaoCaoKhoaDto>>;

public sealed class GetBaoCaoKhoaHandler(IM01ReadRepository repo)
    : IRequestHandler<GetBaoCaoKhoaQuery, Result<BaoCaoKhoaDto>>
{
    public async Task<Result<BaoCaoKhoaDto>> Handle(GetBaoCaoKhoaQuery request, CancellationToken ct)
    {
        var list = await repo.ListKhoaDoanhThuAsync(request.NgayBaoCao, ct);

        if (list.Count == 0)
            return Result.Failure<BaoCaoKhoaDto>(Error.NotFound("BaoCaoKhoa"));

        var tongLuot   = list.Sum(x => x.SoBenhNhan);
        var tongThu    = list.Sum(x => x.TongThu);
        var tongBhyt   = list.Sum(x => x.BhytTra);
        var tongBnTra  = list.Sum(x => x.BnTra);

        var tbBenhNhan = tongLuot > 0 ? Math.Round(tongThu / tongLuot, 0) : 0;

        // Tính số tuần dựa theo khoảng ngày baocao trong tập dữ liệu (tối thiểu 1 tuần)
        var minNgay   = list.Min(x => x.NgayBaoCao);
        var maxNgay   = list.Max(x => x.NgayBaoCao);
        var soTuan    = Math.Max(1, Math.Ceiling((maxNgay - minNgay).TotalDays / 7.0));
        var tbTuan    = Math.Round(tongThu / (decimal)soTuan, 0);

        var tiLeBhyt  = tongThu > 0 ? Math.Round((double)(tongBhyt / tongThu) * 100, 1) : 0;

        var khoaCaoNhat = list.MaxBy(x => x.SoBenhNhan);

        var summary = new BaoCaoSummaryDto(
            TongLuotKham:                      tongLuot,
            TongDoanhThu:                      tongThu,
            TongBhyt:                          tongBhyt,
            TongBnTra:                         tongBnTra,
            DoanhThuTrungBinhTheoBenhNhan:     tbBenhNhan,
            DoanhThuTrungBinhTheoTuan:         tbTuan,
            TiLeBhytPct:                       tiLeBhyt,
            KhoaCaoNhat: khoaCaoNhat is null ? null
                : new KhoaCaoNhatDto(khoaCaoNhat.TenKhoa, khoaCaoNhat.SoBenhNhan));

        var chiTiet = list
            .Select(x => new KhoaDoanhThuDto(
                x.Id, x.TenKhoa, x.SoBenhNhan,
                x.TongThu, x.BhytTra, x.BnTra, x.HaoPhiKhac, x.NgayBaoCao))
            .ToList();

        return new BaoCaoKhoaDto(summary, chiTiet);
    }
}
