using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class DynamicFormDbContextFactory : IDesignTimeDbContextFactory<DynamicFormDbContext>
{
    public DynamicFormDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<DynamicFormDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5434;Database=DynamicFormDb;Username=df_user;Password=df_pass",
                pg => pg.MigrationsAssembly(typeof(DynamicFormDbContext).Assembly.FullName))
            .Options;
        return new DynamicFormDbContext(opts);
    }
}
