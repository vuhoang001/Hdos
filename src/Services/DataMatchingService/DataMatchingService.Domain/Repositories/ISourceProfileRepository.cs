using Hdos.DataMatchingService.Domain.Entities;

namespace Hdos.DataMatchingService.Domain.Repositories;

public interface ISourceProfileRepository
{
    Task AddAsync(SourceProfile profile, CancellationToken ct);

    // Tìm profile theo cặp (sourceSystem, recordType) — khóa duy nhất.
    Task<SourceProfile?> GetBySystemAndTypeAsync(string sourceSystem, string recordType, CancellationToken ct);

    // Lấy tất cả profile, có thể lọc theo sourceSystem để xem một nguồn có những loại nào.
    Task<List<SourceProfile>> GetAllAsync(string? sourceSystem, CancellationToken ct);
}
