using Hdos.Common.Extensions;
using Hdos.Common.Persistence;
using Hdos.Contracts.Grpc.Users;
using Hdos.OrderService.Application.Abstractions;
using Hdos.OrderService.Application.EventHandlers;
using Hdos.OrderService.Domain.Repositories;
using Hdos.OrderService.Infrastructure.Consumers;
using Hdos.OrderService.Infrastructure.Grpc;
using Hdos.OrderService.Infrastructure.Persistence;
using MassTransit;

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
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<OrderCreateRequestedEventHandler>();

        services.AddMassTransitMessaging(configuration, x =>
        {
            x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();
            });

            x.AddConsumer<OrderCreateRequestedConsumer>();
        }, servicePrefix: "order");

        var authGrpcUrl = configuration["Services:Auth:GrpcUrl"] ?? "http://localhost:5111";
        services.AddGrpcClient<UserService.UserServiceClient>(o =>
        {
            o.Address = new Uri(authGrpcUrl);
        });
        services.AddScoped<IUserLookupService, AuthUserLookupClient>();

        return services;
    }
}
