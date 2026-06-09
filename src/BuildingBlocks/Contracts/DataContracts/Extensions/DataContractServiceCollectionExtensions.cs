using Microsoft.Extensions.DependencyInjection;

namespace Hdos.Contracts.DataContracts.Extensions;

/// <summary>
/// DI helpers cho Data Contract layer.
///
/// Convention sử dụng trong Program.cs / DI extension của service:
/// <code>
/// services.AddDataContracts();
/// services.AddDataContract&lt;FinanceDailyContract&gt;();
/// services.AddDataSource&lt;FinanceDailyRow, FinanceDailyViewSource&gt;();
/// services.AddDataSource&lt;FinanceDailyRow, FinanceDailySqlSource&gt;();
/// services.AddDataConsumer&lt;FinanceDailyRow, SduiPage, FinanceDailyChartConsumer&gt;();
/// services.AddDataContractValidator&lt;FinanceDailyRow, FinanceDailyValidator&gt;();
/// </code>
/// </summary>
public static class DataContractServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký Registry (singleton) + Gateway (scoped). Gọi 1 lần per service composition root.
    /// </summary>
    public static IServiceCollection AddDataContracts(this IServiceCollection services)
    {
        services.AddSingleton<DataContractRegistry>();
        services.AddScoped<DataContractGateway>();
        return services;
    }

    public static IServiceCollection AddDataContract<TContract>(this IServiceCollection services)
        where TContract : class, IDataContract
    {
        services.AddSingleton<IDataContract, TContract>();
        return services;
    }

    public static IServiceCollection AddDataSource<TSchema, TSource>(this IServiceCollection services)
        where TSchema : class
        where TSource : class, IDataSource<TSchema>
    {
        services.AddScoped<IDataSource<TSchema>, TSource>();
        return services;
    }

    public static IServiceCollection AddDataConsumer<TSchema, TOutput, TConsumer>(this IServiceCollection services)
        where TSchema : class
        where TConsumer : class, IDataConsumer<TSchema, TOutput>
    {
        services.AddScoped<IDataConsumer<TSchema, TOutput>, TConsumer>();
        return services;
    }

    public static IServiceCollection AddDataContractValidator<TSchema, TValidator>(this IServiceCollection services)
        where TSchema : class
        where TValidator : class, IDataContractValidator<TSchema>
    {
        services.AddScoped<IDataContractValidator<TSchema>, TValidator>();
        return services;
    }
}
