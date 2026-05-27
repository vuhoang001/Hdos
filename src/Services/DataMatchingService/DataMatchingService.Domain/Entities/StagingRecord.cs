using Hdos.DataMatchingService.Domain.Enums;
using Hdos.SharedKernel;

namespace Hdos.DataMatchingService.Domain.Entities;

public sealed class StagingRecord : AggregateRoot<Guid>
{
    public string SourceSystem { get; private set; } = default!;
    public string RawPayload { get; private set; } = default!;
    public string? CanonicalPayload { get; private set; }
    public string? BusinessKey { get; private set; }
    public string PayloadHash { get; private set; } = default!;
    public RecordStatus Status { get; private set; }
    public string? MatchedKey { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    private StagingRecord() { }

    public static StagingRecord Receive(
        string sourceSystem,
        string rawPayload,
        string? canonicalPayload,
        string? businessKey,
        string payloadHash)
    {
        return new StagingRecord
        {
            Id               = Guid.NewGuid(),
            SourceSystem     = sourceSystem,
            RawPayload       = rawPayload,
            CanonicalPayload = canonicalPayload,
            BusinessKey      = businessKey,
            PayloadHash      = payloadHash,
            Status           = RecordStatus.Pending,
            ReceivedAt       = DateTime.UtcNow
        };
    }

    public void MarkProcessing()
    {
        Status       = RecordStatus.Processing;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkMatched(string matchedKey)
    {
        Status       = RecordStatus.Matched;
        MatchedKey   = matchedKey;
        ProcessedAt  = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkDuplicate()
    {
        Status       = RecordStatus.Duplicate;
        ProcessedAt  = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status        = RecordStatus.Failed;
        FailureReason = reason;
        ProcessedAt   = DateTime.UtcNow;
        UpdatedAtUtc  = DateTime.UtcNow;
    }
}
