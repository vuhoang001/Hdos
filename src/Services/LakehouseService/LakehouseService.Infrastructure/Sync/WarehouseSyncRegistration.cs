using Hdos.LakehouseService.Domain.Repositories;
using Hdos.LakehouseService.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Sync;

public static class WarehouseSyncRegistration
{
    /// <summary>
    /// Đăng ký pipeline pull data từ warehouse external vào pipeline DataMatching (qua RabbitMQ).
    /// Yêu cầu env <c>ConnectionStrings__Warehouse</c> đã set.
    /// Nếu không set → skip registration (service vẫn chạy bình thường, chỉ không có sync).
    /// </summary>
    public static IServiceCollection AddWarehouseSync(
        this IServiceCollection services, IConfiguration config)
    {
        var connStr = config.GetConnectionString("Warehouse");
        if (string.IsNullOrWhiteSpace(connStr))
            return services;

        services.AddSingleton(_ => NpgsqlDataSource.Create(connStr));
        services.AddScoped<ISyncStateRepository, SyncStateRepository>();
        services.AddScoped<IViewBindingRepository, ViewBindingRepository>();
        services.AddScoped<IWarehouseViewSyncer, WarehouseViewSyncer>();

        return services;
    }
}
