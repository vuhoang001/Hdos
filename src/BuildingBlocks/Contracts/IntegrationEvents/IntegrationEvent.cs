namespace Hdos.Contracts.IntegrationEvents;

/// <summary>
/// Base cho tất cả integration events nội bộ — theo chuẩn CloudEvents (https://cloudevents.io).
///
/// Mapping:
///   CloudEvents id          → EventId
///   CloudEvents type        → EventType
///   CloudEvents source      → Source      (auto-set bởi MassTransitEventBus)
///   CloudEvents time        → OccurredOnUtc
///   Extension correlationid → CorrelationId (auto-set từ OpenTelemetry TraceId)
///   Extension causationid   → CausationId
///   Extension schemaversion → Version
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Unique ID của event. CloudEvents: id</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Tên class. CloudEvents: type</summary>
    public string EventType => GetType().Name;

    /// <summary>Service publish event này. CloudEvents: source. Auto-set bởi MassTransitEventBus.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Thời điểm sự kiện xảy ra (UTC). CloudEvents: time</summary>
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Trace ID xuyên suốt request qua nhiều services. Auto-set từ Activity.Current.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>EventId của event/command đã gây ra event này. Optional.</summary>
    public string? CausationId { get; init; }

    /// <summary>Phiên bản schema của payload. Tăng khi có breaking change.</summary>
    public string Version { get; init; } = "1.0";
}
