using Hdos.SharedKernel;

namespace Hdos.LakehouseService.Domain.Entities;

public sealed class LakehouseSnapshot : AggregateRoot<Guid>
{
    public string Namespace { get; private set; } = null!;
    public string BusinessKey { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public string JobId { get; private set; } = null!;
    public DateTime ReceivedAt { get; private set; }

    private LakehouseSnapshot() { }

    public static LakehouseSnapshot Create(
        string @namespace,
        string businessKey,
        string payload,
        string jobId)
    {
        return new LakehouseSnapshot
        {
            Id          = Guid.NewGuid(),
            Namespace   = @namespace,
            BusinessKey = businessKey,
            Payload     = payload,
            JobId       = jobId,
            ReceivedAt  = DateTime.UtcNow
        };
    }
}
