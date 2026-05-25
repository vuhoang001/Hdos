using FluentValidation;
using Hdos.Common.Messaging;
using Hdos.M01Service.Application.DTOs;
using Hdos.M01Service.Domain.Entities;
using Hdos.M01Service.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;
using Integration = Hdos.Contracts.IntegrationEvents;

namespace Hdos.M01Service.Application.Features.BaoCao;

public sealed record CreateBaoCaoKhoaCommand(
    string   MaKhoa,
    string   TenKhoa,
    int      SoBenhNhan,
    decimal  TongThu,
    decimal  BhytTra,
    decimal  BnTra,
    decimal  HaoPhiKhac,
    DateTime NgayBaoCao) : IRequest<Result<KhoaDoanhThuDto>>;

public sealed class CreateBaoCaoKhoaValidator : AbstractValidator<CreateBaoCaoKhoaCommand>
{
    public CreateBaoCaoKhoaValidator()
    {
        RuleFor(x => x.MaKhoa).NotEmpty().MaximumLength(32);
        RuleFor(x => x.TenKhoa).NotEmpty().MaximumLength(120);
        RuleFor(x => x.SoBenhNhan).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TongThu).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BhytTra).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BnTra).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HaoPhiKhac).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateBaoCaoKhoaHandler(
    IM01WriteRepository repo,
    IEventBus           eventBus)
    : IRequestHandler<CreateBaoCaoKhoaCommand, Result<KhoaDoanhThuDto>>
{
    public async Task<Result<KhoaDoanhThuDto>> Handle(
        CreateBaoCaoKhoaCommand request, CancellationToken ct)
    {
        var existing = await repo.FindKhoaDoanhThuAsync(request.MaKhoa, ct);
        if (existing is not null)
        {
            existing.Update(request.TenKhoa, request.SoBenhNhan,
                request.TongThu, request.BhytTra, request.BnTra,
                request.HaoPhiKhac, request.NgayBaoCao);
        }
        else
        {
            var entity = new KhoaDoanhThu(
                request.MaKhoa, request.TenKhoa, request.SoBenhNhan,
                request.TongThu, request.BhytTra, request.BnTra,
                request.HaoPhiKhac, request.NgayBaoCao);
            await repo.UpsertKhoaDoanhThuAsync(entity, ct);
        }

        await repo.SaveChangesAsync(ct);

        var (tongLuotKham, tongDoanhThu, tbTuan) = await repo.GetAllTimeTotalsAsync(ct);

        // PublishAsync ghi OutboxMessage vào EF change tracker; SaveChanges bên dưới commit nó
        await eventBus.PublishAsync(
            new Integration.BaoCaoKhoaCreatedIntegrationEvent(
                tongLuotKham, tongDoanhThu, tbTuan, request.NgayBaoCao),
            ct);

        await repo.SaveChangesAsync(ct);  // commit OutboxMessage

        return new KhoaDoanhThuDto(
            request.MaKhoa, request.TenKhoa, request.SoBenhNhan,
            request.TongThu, request.BhytTra, request.BnTra,
            request.HaoPhiKhac, request.NgayBaoCao);
    }
}
