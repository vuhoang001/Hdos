using Hdos.Contracts.DataContracts.Extensions;
using Hdos.Contracts.DataContracts.FormPrefill;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;
using Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;
using Hdos.LakehouseService.Infrastructure.DataContracts.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Registration;

/// <summary>
/// Đăng ký Data Contract layer cho LakehouseService:
///   - Bootstrap Gateway + Registry (1 lần)
///   - Đăng ký từng contract + source + consumer + validator
///
/// Gọi từ <c>AddLakehouseInfrastructure</c>. Mỗi contract mới thêm 1 cụm
/// <c>AddDataContract + AddDataSource* + AddDataConsumer*</c>.
/// </summary>
public static class DataContractsRegistration
{
    public static IServiceCollection AddLakehouseDataContracts(this IServiceCollection services)
    {
        services.AddDataContracts();

        // ── finance.daily.row ──
        services
            .AddDataContract<FinanceDailyContract>()
            .AddDataSource<FinanceDailyRow, FinanceDailySqlSource>()
            .AddDataSource<FinanceDailyRow, FinanceDailyDemoSource>()
            .AddDataConsumer<FinanceDailyRow, SduiPage,         FinanceDailyChartConsumer>()
            .AddDataConsumer<FinanceDailyRow, FormPrefillResult, FinanceDailyFormPrefillConsumer>()
            .AddDataContractValidator<FinanceDailyRow, FinanceDailyValidator>();

        // ── patient.daily.new (demo only — chưa có SQL source) ──
        services
            .AddDataContract<PatientDailyNewContract>()
            .AddDataSource<PatientDailyNewRow, PatientDailyNewDemoSource>()
            .AddDataConsumer<PatientDailyNewRow, SduiPage, PatientDailyNewChartConsumer>()
            .AddDataContractValidator<PatientDailyNewRow, PatientDailyNewValidator>();

        return services;
    }
}
