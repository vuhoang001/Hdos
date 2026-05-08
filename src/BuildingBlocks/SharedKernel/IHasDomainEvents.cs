namespace Hdos.SharedKernel;

/// <summary>
/// Non-generic marker so infrastructure (e.g. DbContext / EF interceptor) can
/// look up domain events on any aggregate without knowing the TId of
/// <see cref="AggregateRoot{TId}"/>.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
