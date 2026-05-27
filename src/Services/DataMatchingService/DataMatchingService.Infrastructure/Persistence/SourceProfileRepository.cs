using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DataMatchingService.Infrastructure.Persistence;

public sealed class SourceProfileRepository(DataMatchingDbContext db) : ISourceProfileRepository
{
    public async Task AddAsync(SourceProfile profile, CancellationToken ct) =>
        await db.SourceProfiles.AddAsync(profile, ct);

    public Task<SourceProfile?> GetBySystemAsync(string sourceSystem, CancellationToken ct) =>
        db.SourceProfiles.FirstOrDefaultAsync(p => p.SourceSystem == sourceSystem, ct);

    public Task<List<SourceProfile>> GetAllAsync(CancellationToken ct) =>
        db.SourceProfiles.OrderBy(p => p.SourceSystem).ToListAsync(ct);
}
