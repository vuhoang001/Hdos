using Hdos.AuthService.Application.Features.SupersetGuestToken;
using Hdos.AuthService.Application.Options;
using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.Repositories;
using Hdos.AuthService.Infrastructure.Persistence;
using Hdos.AuthService.Infrastructure.Superset;
using Hdos.Common.Extensions;
using Hdos.Common.Persistence;
using Microsoft.AspNetCore.Identity;
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
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IUserLicenseRepository, UserLicenseRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddMassTransitMessaging(configuration);

        // ── Superset integration (Phase 2 SSO + Phase 4 guest token) ─────
        services.Configure<SupersetOptions>(configuration.GetSection(SupersetOptions.SectionName));
        services.AddMemoryCache();
        services.AddHttpClient<ISupersetAdminClient, SupersetAdminClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupersetOptions>>().Value;
            var baseUrl = opts.BaseUrl;
            if (!baseUrl.EndsWith('/')) baseUrl += "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
