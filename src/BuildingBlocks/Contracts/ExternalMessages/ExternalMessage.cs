namespace Hdos.Contracts.ExternalMessages;

/// <summary>
/// Base cho tất cả messages nhận từ hệ thống bên ngoài qua RabbitMQ.
/// Theo chuẩn CloudEvents (https://cloudevents.io) — tất cả fields nullable
/// vì chúng ta không kiểm soát được format của bên ngoài.
///
/// Mapping với CloudEvents spec:
///   id            → MessageId
///   source        → Source
///   type          → EventType    (tên sự kiện do bên ngoài định nghĩa)
///   time          → OccurredAt
///   correlationid → CorrelationId
///   schemaversion → Version
///
/// Cách dùng: Consumer mới kế thừa record này và thêm các fields riêng của payload.
/// </summary>
public abstract record ExternalMessage
{
    /// <summary>Unique ID của message do hệ thống ngoài cấp. CloudEvents: id</summary>
    public string? MessageId { get; init; }

    /// <summary>Hệ thống đã gửi message. CloudEvents: source. Ví dụ: "his-01", "erp-system"</summary>
    public string? Source { get; init; }

    /// <summary>Loại sự kiện do hệ thống ngoài định nghĩa. CloudEvents: type</summary>
    public string? EventType { get; init; }

    /// <summary>Thời điểm sự kiện xảy ra ở hệ thống ngoài. CloudEvents: time</summary>
    public DateTime? OccurredAt { get; init; }

    /// <summary>Dùng để trace request xuyên hệ thống. Nếu bên ngoài không gửi, để null.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Phiên bản schema của message. Dùng khi cần handle nhiều version.</summary>
    public string? Version { get; init; }
}
