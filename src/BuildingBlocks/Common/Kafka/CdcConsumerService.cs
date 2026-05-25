using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Kafka;

/// <summary>
/// Production-grade BackgroundService để consume CDC events từ Kafka topic do Debezium publish.
///
/// Delivery: at-least-once.
/// - StoreOffset sau HandleAsync → auto-commit flush mỗi AutoCommitIntervalMs (batch, không per-message)
/// - SetPartitionsRevokedHandler commit ngay khi rebalance để tránh replay quá nhiều
/// - HandleAsync PHẢI idempotent — Debezium có thể replay khi connector restart
///
/// Threading: Kafka Consume() là blocking call.
/// Dùng Task.Factory.StartNew + LongRunning = dedicated OS thread, tránh ThreadPool starvation.
/// </summary>
public abstract class CdcConsumerService<TEntity> : BackgroundService
{
    // static readonly: tránh allocate JsonSerializerOptions mới mỗi message (GC pressure)
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger _logger;
    private IConsumer<string, string>? _consumer;

    protected CdcConsumerService(ILogger logger)
    {
        _logger = logger;
    }

    protected abstract KafkaConsumerOptions Options { get; }

    /// <summary>
    /// Xử lý một CDC event. PHẢI idempotent.
    /// Throw exception → offset không được store → message replay sau khi service restart.
    /// </summary>
    protected abstract Task HandleAsync(DebeziumPayload<TEntity> payload, CancellationToken ct);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Factory.StartNew(
            () => RunConsumerLoop(stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void RunConsumerLoop(CancellationToken stoppingToken)
    {
        _consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers      = Options.BootstrapServers,
            GroupId               = Options.GroupId,
            AutoOffsetReset       = AutoOffsetReset.Earliest,

            // StoreOffset() sau HandleAsync → auto-commit batch flush (không round-trip per message)
            EnableAutoOffsetStore = false,
            EnableAutoCommit      = true,
            AutoCommitIntervalMs  = Options.AutoCommitIntervalMs,

            // Explicit để tránh silent rebalance khi HandleAsync chậm hơn default timeout
            SessionTimeoutMs      = Options.SessionTimeoutMs,
            MaxPollIntervalMs     = Options.MaxPollIntervalMs,
        })
        .SetPartitionsRevokedHandler((c, partitions) =>
        {
            // Kafka gọi đây TRƯỚC khi giao partition cho consumer khác.
            // Commit ngay để tránh replay từ đầu sau rebalance.
            _logger.LogWarning(
                "Partitions revoked — flushing offsets. Partitions: [{P}]",
                string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition}]")));
            try { c.Commit(); }
            catch (KafkaException ex) { _logger.LogError(ex, "Commit on revoke failed"); }
        })
        .SetPartitionsAssignedHandler((_, partitions) =>
        {
            _logger.LogInformation(
                "Partitions assigned: [{P}]",
                string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition}]")));
        })
        .Build();

        _consumer.Subscribe(Options.Topic);
        _logger.LogInformation(
            "CDC consumer subscribed — topic={Topic} group={Group}",
            Options.Topic, Options.GroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = _consumer.Consume(TimeSpan.FromMilliseconds(Options.ConsumeTimeoutMs));
                if (result?.Message?.Value is null) continue;

                var envelope = JsonSerializer.Deserialize<DebeziumEnvelope<TEntity>>(
                    result.Message.Value, JsonOptions);

                if (envelope?.Payload is null) continue;

                _logger.LogDebug(
                    "CDC [{Op}] {Table} lsn={Lsn} offset={Offset}",
                    envelope.Payload.Op,
                    envelope.Payload.Source?.Table,
                    envelope.Payload.Source?.Lsn,
                    result.Offset.Value);

                // LongRunning thread không có SynchronizationContext → GetAwaiter().GetResult() an toàn
                HandleAsync(envelope.Payload, stoppingToken).GetAwaiter().GetResult();

                // Mark offset sẵn sàng — auto-commit thread flush sau AutoCommitIntervalMs
                _consumer.StoreOffset(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex) when (ex.Error.IsFatal)
            {
                _logger.LogCritical(ex, "Fatal Kafka error — consumer shutting down");
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Transient Kafka consume error on topic {Topic}", Options.Topic);
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "HandleAsync failed — topic={Topic} offset={Offset} — offset NOT stored, will replay on restart",
                    Options.Topic, result?.Offset.Value);
                Thread.Sleep(500);
            }
        }

        _consumer.Close();
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}
