using Hdos.DataMatchingService.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Hdos.DataMatchingService.Infrastructure.Workers;

public sealed class MatchingWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MatchingWorker> logger) : BackgroundService
{
    private readonly int _intervalSeconds =
        configuration.GetValue("Matching:WorkerIntervalSeconds", 30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MatchingWorker started. Interval: {Interval}s", _intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "MatchingWorker encountered an unhandled error during batch processing.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }

        logger.LogInformation("MatchingWorker stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var records = scope.ServiceProvider.GetRequiredService<IStagingRecordRepository>();
        var uow     = scope.ServiceProvider.GetRequiredService<IDataMatchingUnitOfWork>();

        var batch = await records.GetPendingBatchAsync(50, ct);
        if (batch.Count == 0) return;

        var processed = 0;

        foreach (var record in batch)
        {
            try
            {
                record.MarkProcessing();

                var matchedKey = string.IsNullOrWhiteSpace(record.BusinessKey)
                    ? $"{record.SourceSystem}::{record.PayloadHash}"
                    : $"{record.SourceSystem}::{record.BusinessKey}";

                record.MarkMatched(matchedKey);
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to match record {Id}", record.Id);
                record.MarkFailed(ex.Message);
            }
        }

        await uow.SaveChangesAsync(ct);

        logger.LogInformation(
            "MatchingWorker processed {Count} records in this batch.", processed);
    }
}
