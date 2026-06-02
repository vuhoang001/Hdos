using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hdos.AuthService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory cho EF Core CLI (<c>dotnet ef migrations add/remove/script</c>).
/// </summary>
/// <remarks>
/// <b>Tại sao cần factory này?</b><br/>
/// EF tools cần instantiate <see cref="AuthDbContext"/> để đọc schema khi tạo migration.
/// Nếu dùng startup project (<c>AuthService.API</c>), nó sẽ cố kết nối SQL Server thật
/// — fail khi chạy local không có SQL Server.
/// Factory này cung cấp connection string giả — EF chỉ đọc schema, <b>không cần DB thật</b>.
///
/// <b>Cách chạy migration:</b>
/// <code>
/// export DOTNET_ROOT="$HOME/.dotnet"
/// export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
///
/// dotnet ef migrations add &lt;TênMigration&gt; \
///   --project src/Services/AuthService/AuthService.Infrastructure \
///   --startup-project src/Services/AuthService/AuthService.Infrastructure
/// </code>
///
/// Migration được apply tự động khi app khởi động trên server
/// qua <c>db.Database.MigrateAsync()</c> trong <c>Program.cs</c>.
/// </remarks>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    /// <summary>
    /// Tạo <see cref="AuthDbContext"/> với connection string giả cho EF tools.
    /// Không dùng trong runtime — chỉ dùng bởi <c>dotnet ef</c>.
    /// </summary>
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
