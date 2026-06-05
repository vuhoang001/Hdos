using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IOperationRepository
{
    Task AddAsync(Operation operation, CancellationToken ct);

    Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct);

    // Lookup chính khi resolve DataSource.OperationId.
    Task<Operation?> GetByKeyAsync(string providerCode, string operationKey, CancellationToken ct);

    // Liệt kê operations của một provider (CRUD cấp provider).
    Task<List<Operation>> GetByProviderAsync(string providerCode, CancellationToken ct);

    // Cross-provider list cho dropdown FE.
    Task<List<Operation>> GetAllAsync(OperationStatus? status, CancellationToken ct);

    Task<bool> ExistsByKeyAsync(string providerCode, string operationKey, CancellationToken ct);

    // Dùng cho rule cấm xóa Provider khi còn Operation tham chiếu.
    Task<bool> AnyByProviderAsync(string providerCode, CancellationToken ct);

    void Remove(Operation operation);
}
