using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hdos.LakehouseService.Infrastructure.Persistence;

public sealed class LakehouseDbContextFactory : IDesignTimeDbContextFactory<LakehouseDbContext>
{
    public LakehouseDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<LakehouseDbContext>()
            .UseNpgsql("Host=localhost;Port=5435;Database=LakehouseDb;Username=lh_user;Password=lh_pass")
            .Options;
        return new LakehouseDbContext(opts);
    }
}
