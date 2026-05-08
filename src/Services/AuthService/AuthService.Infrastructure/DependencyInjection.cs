using Hdos.AuthService.Application.Abstractions;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Infrastructure.Persistence;
using Hdos.AuthService.Infrastructure.Security;
using Hdos.Common.Extensions;
using Hdos.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.AuthService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDomainEventDispatching();

        services.AddDbContext<AuthDbContext>((sp, opts) =>
            opts.UseSqlServer(
                    configuration.GetConnectionString("AuthDb"),
                    sql => sql.MigrationsAssembly(typeof(AuthDbContext).Assembly.FullName))
                .AddInterceptors(sp.GetRequiredService<PublishDomainEventsInterceptor>()));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddRabbitMq(configuration);
        return services;
    }
}
