using Hdos.SharedKernel;

namespace Hdos.NotificationService.Domain.Entities;

public enum NotificationChannel { Email = 0, Sms = 1, Push = 2 }
public enum NotificationStatus { Pending = 0, Sent = 1, Failed = 2 }

public sealed class Notification : AggregateRoot<Guid>
{
    public string Recipient { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime? SentAtUtc { get; private set; }

    private Notification() { }

    public static Notification Create(string recipient, string subject, string body,
        NotificationChannel channel = NotificationChannel.Email)
    {
        if (string.IsNullOrWhiteSpace(recipient)) throw new ArgumentException("recipient required");
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("subject required");

        return new Notification
        {
            Id = Guid.NewGuid(),
            Recipient = recipient.Trim(),
            Subject = subject.Trim(),
            Body = body ?? string.Empty,
            Channel = channel,
            Status = NotificationStatus.Pending
        };
    }

    public void MarkSent()
    {
        Status = NotificationStatus.Sent;
        SentAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = NotificationStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
