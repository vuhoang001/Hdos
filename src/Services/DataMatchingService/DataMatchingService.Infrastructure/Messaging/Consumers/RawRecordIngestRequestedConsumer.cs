using Hdos.Contracts.IntegrationEvents;
using Hdos.DataMatchingService.Application.Services;
using Hdos.DataMatchingService.Domain.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Hdos.DataMatchingService.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consume <c>RawRecordIngestRequestedIntegrationEvent</c> từ bất kỳ Source Provider nào
/// (LakehouseService poll PG view, connector Excel/CSV future, ...). Reuse cùng pipeline
/// canonical hóa với REST <c>/dm/ingest/json</c> qua <see cref="IIngestCoreService"/>.
///
/// Hành vi với dedup:
///   • Payload trùng SHA-256 → log debug + ack message (không retry, không lưu).
///   • SourceProfile chưa đăng ký → log warning + ack message (poison message để admin fix
///     SourceProfile, không retry vô tận).
///   • Mọi lỗi khác → throw để MassTransit retry theo policy (5 lần exponential).
///
/// Xem doc 44 — Unified Ingest Pipeline §3, §5.
/// </summary>
public sealed class RawRecordIngestRequestedConsumer(
    IIngestCoreService          core,
    IStagingRecordRepository    records,
    IDataMatchingUnitOfWork     uow,
    ILogger<RawRecordIngestRequestedConsumer> logger)
    : IConsumer<RawRecordIngestRequestedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<RawRecordIngestRequestedIntegrationEvent> context)
    {
        var msg = context.Message;
        var businessKeyOverride = string.IsNullOrWhiteSpace(msg.BusinessKey) ? null : msg.BusinessKey;

        var built = await core.TryBuildRecordAsync(
            msg.SourceSystem, msg.RecordType, msg.RawPayloadJson, businessKeyOverride,
            context.CancellationToken);

        if (built.IsFailure)
        {
            logger.LogWarning(
                "Ingest skipped {Source}/{Type} (BK={Key}, Job={Job}): {Error}",
                msg.SourceSystem, msg.RecordType, msg.BusinessKey, msg.SourceJobId, built.Error.Message);
            return;
        }

        if (built.Value is not { } record)
        {
            logger.LogDebug(
                "Ingest dedup hit {Source}/{Type} (BK={Key}, Job={Job})",
                msg.SourceSystem, msg.RecordType, msg.BusinessKey, msg.SourceJobId);
            return;
        }

        await records.AddAsync(record, context.CancellationToken);
        await uow.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Ingest accepted {Source}/{Type} (BK={Key}, RecordId={RecordId}, Job={Job})",
            msg.SourceSystem, msg.RecordType, msg.BusinessKey, record.Id, msg.SourceJobId);
    }
}
