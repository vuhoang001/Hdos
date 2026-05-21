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
        logger.LogInformation("Broadcasting bao cao khoa summary for {NgayBaoCao}", @event.NgayBaoCao);

        await pusher.BroadcastEventAsync(
            "bao_cao_khoa_summary",
            new
            {
                tongLuotKham              = @event.TongLuotKham,
                tongDoanhThu              = @event.TongDoanhThu,
                doanhThuTrungBinhTheoTuan = @event.DoanhThuTrungBinhTheoTuan,
                ngayBaoCao                = @event.NgayBaoCao
            },
            ct);
    }
}
