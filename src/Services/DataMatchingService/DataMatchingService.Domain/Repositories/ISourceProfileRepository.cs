using Hdos.DataMatchingService.Domain.Entities;

namespace Hdos.DataMatchingService.Domain.Repositories;

public interface ISourceProfileRepository
{
    Task AddAsync(SourceProfile profile, CancellationToken ct);
    Task<SourceProfile?> GetBySystemAsync(string sourceSystem, CancellationToken ct);
    Task<List<SourceProfile>> GetAllAsync(CancellationToken ct);
}
