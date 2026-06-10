using System.Runtime.CompilerServices;
using Hdos.Contracts.DataContracts;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Sources;

// In-memory source: 12 tháng × 3 khoa = 36 row cố định. Dùng cho demo doc 59.
// Query filter: ?year=&month=&department=
public sealed class FinanceMonthlyDemoSource : IDataSource<FinanceMonthlyRow>
{
    public string ContractCode => FinanceMonthlyContract.ContractCode;
    public string SourceCode   => "demo";

    public async IAsyncEnumerable<FinanceMonthlyRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;

        var year      = query.GetInt("year")       ?? 2026;
        var monthFilt = query.GetInt("month");
        var deptFilt  = query.GetInt("department");

        var depts = new (int Id, string Name, decimal BaseRevenue, decimal CostRatio, int BasePatients)[]
        {
            (1, "Khoa Tim mạch",         900_000_000m, 0.55m, 1200),
            (2, "Khoa Hồi sức tích cực", 700_000_000m, 0.70m,  800),
            (3, "Khoa Nhi",              500_000_000m, 0.50m, 1500),
        };

        foreach (var dept in depts)
        {
            if (deptFilt is int d && d != dept.Id) continue;

            for (var month = 1; month <= 12; month++)
            {
                if (monthFilt is int m && m != month) continue;
                ct.ThrowIfCancellationRequested();

                // Seasonality: tháng 1+12 cao (+25%), tháng 6-8 thấp (-15%).
                decimal mult = month is 1 or 12 ? 1.25m
                            : month is >= 6 and <= 8 ? 0.85m
                            : 1.0m;

                var revenue  = dept.BaseRevenue * mult;
                var cost     = revenue * dept.CostRatio;
                var patients = (int)(dept.BasePatients * (double)mult);

                yield return new FinanceMonthlyRow(
                    Year:           year,
                    Month:          month,
                    DepartmentId:   dept.Id,
                    DepartmentName: dept.Name,
                    TotalRevenue:   Math.Round(revenue, 0),
                    TotalCost:      Math.Round(cost, 0),
                    PatientCount:   patients);
            }
        }
    }
}
