using Hdos.LakehouseService.Domain.Entities;

namespace Hdos.LakehouseService.Domain.Repositories;

public interface IViewBindingRepository
{
    Task<ViewBinding?>      GetByIdAsync(Guid id, CancellationToken ct);
    Task<ViewBinding?>      GetByViewNameAsync(string viewName, CancellationToken ct);
    Task<List<ViewBinding>> ListAsync(CancellationToken ct);
    Task<List<ViewBinding>> ListActiveAsync(CancellationToken ct);

    Task AddAsync(ViewBinding binding, CancellationToken ct);
    Task RemoveAsync(ViewBinding binding, CancellationToken ct);

    /// <summary>Lưu các thay đổi tracked vào DB.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
