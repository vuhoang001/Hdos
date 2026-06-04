using Hdos.LakehouseService.Domain.Entities;

namespace Hdos.LakehouseService.Domain.Repositories;

public interface ILakehouseSnapshotRepository
{
    Task AddAsync(LakehouseSnapshot snapshot, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<LakehouseSnapshot> snapshots, CancellationToken ct);
    Task<LakehouseSnapshot?> GetLatestAsync(string @namespace, string businessKey, CancellationToken ct);
    Task<List<LakehouseSnapshot>> GetByNamespaceAsync(string @namespace, int limit, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
