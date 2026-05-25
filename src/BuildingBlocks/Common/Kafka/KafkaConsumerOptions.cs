namespace Hdos.Common.Kafka;

public sealed class KafkaConsumerOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";

    /// <summary>Consumer group ID — mỗi service dùng group riêng để đọc từ đầu topic.</summary>
    public string GroupId { get; init; } = "hdos-cdc-consumer";

    /// <summary>Topic Debezium publish: {prefix}.{database}.{schema}.{table}</summary>
    public string Topic { get; init; } = default!;

    /// <summary>Số ms chờ message trước khi check cancellation token.</summary>
    public int ConsumeTimeoutMs { get; init; } = 200;
}
