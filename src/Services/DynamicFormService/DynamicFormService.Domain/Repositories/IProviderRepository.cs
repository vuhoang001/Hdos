using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IProviderRepository
{
    Task AddAsync(Provider provider, CancellationToken ct);

    Task<Provider?> GetByIdAsync(Guid id, CancellationToken ct);

    // Lookup phổ biến — DataSource resolve qua code.
    Task<Provider?> GetByCodeAsync(string code, CancellationToken ct);

    // status = null → trả về tất cả; có giá trị → filter.
    Task<List<Provider>> GetAllAsync(ProviderStatus? status, CancellationToken ct);

    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct);

    void Remove(Provider provider);
}
