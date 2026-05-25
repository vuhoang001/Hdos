using System.Text.Json.Serialization;

namespace Hdos.Common.Kafka;

/// <summary>
/// Debezium message envelope — outer wrapper của mọi CDC event từ Kafka.
/// </summary>
public sealed class DebeziumEnvelope<T>
{
    [JsonPropertyName("payload")]
    public DebeziumPayload<T>? Payload { get; init; }
}

public sealed class DebeziumPayload<T>
{
    /// <summary>Snapshot trước khi thay đổi. Null với INSERT.</summary>
    [JsonPropertyName("before")]
    public T? Before { get; init; }

    /// <summary>Snapshot sau khi thay đổi. Null với DELETE.</summary>
    [JsonPropertyName("after")]
    public T? After { get; init; }

    /// <summary>"c"=insert, "u"=update, "d"=delete, "r"=snapshot read.</summary>
    [JsonPropertyName("op")]
    public string Op { get; init; } = default!;

    [JsonPropertyName("source")]
    public DebeziumSource? Source { get; init; }

    [JsonPropertyName("ts_ms")]
    public long TimestampMs { get; init; }

    public CdcOperation Operation => Op switch
    {
        "c" => CdcOperation.Created,
        "u" => CdcOperation.Updated,
        "d" => CdcOperation.Deleted,
        "r" => CdcOperation.Snapshot,
        _   => CdcOperation.Unknown
    };

    public DateTime OccurredAtUtc =>
        DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs).UtcDateTime;
}

public sealed class DebeziumSource
{
    [JsonPropertyName("db")]
    public string Database { get; init; } = default!;

    [JsonPropertyName("schema")]
    public string Schema { get; init; } = default!;

    [JsonPropertyName("table")]
    public string Table { get; init; } = default!;

    [JsonPropertyName("ts_ms")]
    public long CommitTimestampMs { get; init; }

    /// <summary>
    /// Log Sequence Number — vị trí chính xác trong SQL Server transaction log.
    /// Dùng làm idempotency key: (Table, Lsn) là unique per change event.
    /// </summary>
    [JsonPropertyName("change_lsn")]
    public string? Lsn { get; init; }

    [JsonPropertyName("commit_lsn")]
    public string? CommitLsn { get; init; }
}

public enum CdcOperation { Created, Updated, Deleted, Snapshot, Unknown }
