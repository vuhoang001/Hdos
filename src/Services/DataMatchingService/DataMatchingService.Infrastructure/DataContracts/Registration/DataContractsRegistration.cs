using Hdos.Contracts.DataContracts.Extensions;
using Hdos.Contracts.DataContracts.Finance;
using Hdos.DataMatchingService.Infrastructure.DataContracts.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.DataMatchingService.Infrastructure.DataContracts.Registration;

/// <summary>
/// Đăng ký Data Contract layer cho DataMatchingService.
/// Mỗi service có Gateway/Registry riêng — DataMatching có source "staging" đọc từ StagingRecord;
/// Lakehouse có source "sql"/"demo" đọc từ raw tables. Cùng schema, khác source.
/// </summary>
public static class DataContractsRegistration
{
    public static IServiceCollection AddDataMatchingDataContracts(this IServiceCollection services)
    {
        services.AddDataContracts();

        // ── finance.daily.row ─ với source "staging" (đọc canonical từ StagingRecord)
        services
            .AddDataContract<FinanceDailyContract>()
            .AddDataSource<FinanceDailyRow, FinanceDailyStagingSource>()
            .AddDataContractValidator<FinanceDailyRow, FinanceDailyValidator>();

        return services;
    }
}
