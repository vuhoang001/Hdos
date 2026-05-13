namespace Hdos.AsyncGateway.API.Models;

public sealed record AsyncResponse(Guid CorrelationId, string Status = "queued");
