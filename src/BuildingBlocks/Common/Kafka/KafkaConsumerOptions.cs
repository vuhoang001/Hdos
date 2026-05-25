namespace Hdos.Common.Kafka;

public sealed class KafkaConsumerOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";

    /// <summary>Consumer group ID — mỗi service dùng group riêng để đọc từ đầu topic.</summary>
    public string GroupId { get; init; } = "hdos-cdc-consumer";

    /// <summary>Topic Debezium publish: {prefix}.{database}.{schema}.{table}</summary>
    public string Topic { get; init; } = default!;

    /// <summary>Số ms chờ message trước khi check stoppingToken khi topic idle.</summary>
    public int ConsumeTimeoutMs { get; init; } = 500;

    /// <summary>
    /// Kafka auto-commit interval (ms). Offset được flush mỗi khoảng này thay vì per-message.
    /// Tăng → ít round-trip hơn, tăng replay window khi crash.
    /// </summary>
    public int AutoCommitIntervalMs { get; init; } = 5000;

    /// <summary>
    /// Thời gian tối đa broker chờ heartbeat trước khi coi consumer là dead.
    /// Nếu HandleAsync có thể chậm hơn 10s, tăng giá trị này.
    /// </summary>
    public int SessionTimeoutMs { get; init; } = 30000;

    /// <summary>
    /// Thời gian tối đa giữa 2 lần Consume() call.
    /// Nếu HandleAsync chậm hơn giá trị này → Kafka kick consumer khỏi group.
    /// Phải > SessionTimeoutMs.
    /// </summary>
    public int MaxPollIntervalMs { get; init; } = 300000;
}
