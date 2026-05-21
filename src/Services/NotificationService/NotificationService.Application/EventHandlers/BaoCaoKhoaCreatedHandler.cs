using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.NotificationService.Application.Realtime;
using Microsoft.Extensions.Logging;

namespace Hdos.NotificationService.Application.EventHandlers;

public sealed class BaoCaoKhoaCreatedHandler(
    INotificationPusher pusher,
    ILogger<BaoCaoKhoaCreatedHandler> logger)
    : IIntegrationEventHandler<BaoCaoKhoaCreatedIntegrationEvent>
{
    public async Task HandleAsync(BaoCaoKhoaCreatedIntegrationEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Broadcasting bao cao khoa created: {MaKhoa}", @event.MaKhoa);

        await pusher.BroadcastEventAsync(
            "bao_cao_khoa_created",
            new
            {
                maKhoa          = @event.MaKhoa,
                tenKhoa         = @event.TenKhoa,
                soBenhNhan      = @event.SoBenhNhan,
                tongThu         = @event.TongThu,
                ngayBaoCao      = @event.NgayBaoCao,
                tongDoanhThuNgay = @event.TongDoanhThuNgay
            },
            ct);
    }
}
