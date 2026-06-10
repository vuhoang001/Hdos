using Hdos.Contracts.Grpc.DataContractRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Infrastructure.Sync;

// Hosted service chạy 1 lần khi service startup: build payload từ
// LakehouseContractOperationCatalog → gọi rpc SyncRegistry sang DynamicForm.
//
// Retry/backoff: DynamicForm có thể chưa lên khi Lakehouse start. Vòng lặp tối
// đa MaxAttempts × DelaySeconds; sau đó log error và bỏ qua — Lakehouse vẫn
// serve các endpoint /lakehouse/contracts/* bình thường, chỉ là DynamicForm
// chưa biết về catalog cho tới lần restart kế tiếp.
//
// Chạy nền (Task.Run trong StartAsync) để không block app startup.
internal sealed class LakehouseRegistrySyncHostedService(
    LakehouseContractOperationCatalog              catalog,
    IServiceProvider                               services,
    IConfiguration                                 configuration,
    ILogger<LakehouseRegistrySyncHostedService>    logger)
    : IHostedService
{
    private const int MaxAttempts  = 30;
    private const int DelaySeconds = 5;

    private CancellationTokenSource? _cts;
    private Task?                    _runner;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runner = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_runner is not null)
        {
            try   { await _runner; }
            catch { /* swallowed: cancellation expected */ }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        if (catalog.Entries.Count == 0)
        {
            logger.LogInformation("Lakehouse operation catalog empty — skip sync");
            return;
        }

        var baseUrl   = configuration["Services:Lakehouse:PublicBaseUrl"]
                        ?? "http://lakehouseservice:8080";
        var request = BuildRequest(baseUrl);

        for (var attempt = 1; attempt <= MaxAttempts && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope  = services.CreateScope();
                var       client = scope.ServiceProvider
                    .GetRequiredService<DataContractRegistrySyncService.DataContractRegistrySyncServiceClient>();

                var reply = await client.SyncRegistryAsync(request, cancellationToken: ct);
                logger.LogInformation(
                    "Lakehouse → DynamicForm registry sync OK: upsertedOps={Ops} deactivated={Dead} (attempt {Attempt})",
                    reply.UpsertedOperationCount, reply.DeactivatedOperationCount, attempt);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Lakehouse → DynamicForm registry sync attempt {Attempt}/{Max} failed",
                    attempt, MaxAttempts);
                try { await Task.Delay(TimeSpan.FromSeconds(DelaySeconds), ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        logger.LogError(
            "Lakehouse → DynamicForm registry sync gave up after {Max} attempts. Restart service to retry.",
            MaxAttempts);
    }

    private SyncRegistryRequest BuildRequest(string baseUrl)
    {
        var request = new SyncRegistryRequest
        {
            Provider = new ProviderSpec
            {
                Code        = "lakehouse",
                DisplayName = "Hdos Lakehouse Service",
                BaseUrl     = baseUrl
            }
        };

        foreach (var entry in catalog.Entries)
        {
            var op = new OperationSpec
            {
                OperationKey = entry.OperationKey,
                DisplayName  = entry.DisplayName,
                Pattern      = entry.Pattern,
                SchemaPath   = entry.SchemaPath ?? string.Empty,
                Kind         = entry.Kind
            };
            op.RequiredParams.AddRange(entry.RequiredParams);
            request.Operations.Add(op);
        }

        return request;
    }
}
