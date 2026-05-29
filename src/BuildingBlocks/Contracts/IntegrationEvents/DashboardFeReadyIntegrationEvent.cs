namespace Hdos.Contracts.IntegrationEvents;

public sealed record DashboardFeReadyIntegrationEvent(
    int      ProcessedCount,
    string[] AffectedSystems)
    : IntegrationEvent;
