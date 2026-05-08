using Hdos.Common.Extensions;
using Hdos.Common.Persistence;
using Hdos.Contracts.Grpc.Users;
using Hdos.OrderService.Application.Abstractions;
using Hdos.OrderService.Domain.Repositories;
using Hdos.OrderService.Infrastructure.Grpc;
using Hdos.OrderService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.OrderService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDomainEventDispatching();

        services.AddDbContext<OrderDbContext>((sp, opts) =>
            opts.UseSqlServer(
                    configuration.GetConnectionString("OrderDb"),
                    sql => sql.MigrationsAssembly(typeof(OrderDbContext).Assembly.FullName))
                .AddInterceptors(sp.GetRequiredService<PublishDomainEventsInterceptor>()));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddRabbitMq(configuration);

        // gRPC client to AuthService — base address is read from config so docker
        // and local dev both work without code changes.
        var authGrpcUrl = configuration["Services:Auth:GrpcUrl"]
                          ?? "http://localhost:5111";
        services.AddGrpcClient<UserService.UserServiceClient>(o =>
        {
            o.Address = new Uri(authGrpcUrl);
        });
        services.AddScoped<IUserLookupService, AuthUserLookupClient>();

        return services;
    }
}
