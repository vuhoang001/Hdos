using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DataMatchingService.Infrastructure.Persistence;

public sealed class SourceProfileRepository(DataMatchingDbContext db) : ISourceProfileRepository
{
    public async Task AddAsync(SourceProfile profile, CancellationToken ct) =>
        await db.SourceProfiles.AddAsync(profile, ct);

    public Task<SourceProfile?> GetBySystemAndTypeAsync(string sourceSystem, string recordType, CancellationToken ct) =>
        db.SourceProfiles.FirstOrDefaultAsync(
            p => p.SourceSystem == sourceSystem && p.RecordType == recordType, ct);

    public Task<List<SourceProfile>> GetAllAsync(string? sourceSystem, CancellationToken ct)
    {
        var query = db.SourceProfiles.AsQueryable();
        if (!string.IsNullOrEmpty(sourceSystem))
            query = query.Where(p => p.SourceSystem == sourceSystem);
        return query.OrderBy(p => p.SourceSystem).ThenBy(p => p.RecordType).ToListAsync(ct);
    }
}
