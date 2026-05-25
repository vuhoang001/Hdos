using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hdos.Common.Kafka;

/// <summary>
/// Base BackgroundService để consume CDC events từ Kafka topic do Debezium publish.
/// Subclass cần implement <see cref="HandleAsync"/> và cung cấp <see cref="Options"/>.
/// </summary>
public abstract class CdcConsumerService<TEntity> : BackgroundService
{
    private readonly ILogger _logger;
    private IConsumer<string, string>? _consumer;

    protected CdcConsumerService(ILogger logger)
    {
        _logger = logger;
    }

    protected abstract KafkaConsumerOptions Options { get; }

    /// <summary>
    /// Xử lý một CDC event. Chạy sau khi deserialize thành công.
    /// Nếu throw exception, message sẽ không được commit (retry ở lần poll tiếp theo).
    /// </summary>
    protected abstract Task HandleAsync(DebeziumPayload<TEntity> payload, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers    = Options.BootstrapServers,
            GroupId             = Options.GroupId,
            AutoOffsetReset     = AutoOffsetReset.Earliest,
            EnableAutoCommit    = false,
        }).Build();

        _consumer.Subscribe(Options.Topic);
        _logger.LogInformation("CDC consumer subscribed to topic {Topic}", Options.Topic);

        await Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = _consumer.Consume(TimeSpan.FromMilliseconds(Options.ConsumeTimeoutMs));
                    if (result?.Message?.Value is null) continue;

                    var envelope = JsonSerializer.Deserialize<DebeziumEnvelope<TEntity>>(
                        result.Message.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (envelope?.Payload is null) continue;

                    _logger.LogDebug(
                        "CDC [{Op}] {Table} offset={Offset}",
                        envelope.Payload.Op,
                        envelope.Payload.Source?.Table,
                        result.Offset.Value);

                    await HandleAsync(envelope.Payload, stoppingToken);

                    _consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error on topic {Topic}", Options.Topic);
                    await Task.Delay(1000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error handling CDC message on topic {Topic} offset {Offset}",
                        Options.Topic,
                        result?.Offset.Value);
                    // không commit → Kafka tự retry từ offset cũ sau khi restart
                    await Task.Delay(500, stoppingToken);
                }
            }
        }, stoppingToken);

        _consumer.Close();
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}
