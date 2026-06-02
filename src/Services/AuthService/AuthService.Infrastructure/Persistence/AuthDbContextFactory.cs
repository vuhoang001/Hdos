using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hdos.AuthService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory cho EF Core tools (dotnet ef migrations add/remove/script).
/// Dùng connection string giả — EF chỉ đọc schema, không cần SQL Server thật khi chạy local.
/// Apply migration thật sự xảy ra lúc app khởi động qua MigrateAsync() trong Program.cs.
/// </summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=AuthDb;User Id=sa;Password=Dev_Pass!;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly(typeof(AuthDbContext).Assembly.FullName))
            .Options;

        return new AuthDbContext(opts);
    }
}
