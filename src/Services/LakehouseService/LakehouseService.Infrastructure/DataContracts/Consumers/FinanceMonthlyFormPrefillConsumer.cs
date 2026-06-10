using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.FormPrefill;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

// Stream FinanceMonthlyRow → flat dict cho FormPrefill.
// Khi ?mode=single → trả `single` object (FE bind expression {{sources.<ns>.<field>}}).
public sealed class FinanceMonthlyFormPrefillConsumer
    : IDataConsumer<FinanceMonthlyRow, FormPrefillResult>
{
    public string ContractCode => FinanceMonthlyContract.ContractCode;
    public string ConsumerCode => "form-prefill";

    public async Task<FormPrefillResult> ConsumeAsync(
        IAsyncEnumerable<FinanceMonthlyRow> stream,
        DataContractQuery                   query,
        CancellationToken                   ct)
    {
        var limit = query.GetInt("limit") ?? 50;
        var rows  = new List<IReadOnlyDictionary<string, object?>>();

        await foreach (var r in stream.WithCancellation(ct))
        {
            var net   = r.TotalRevenue - r.TotalCost;
            var avgRv = r.PatientCount > 0 ? Math.Round(r.TotalRevenue / r.PatientCount, 0) : 0m;

            rows.Add(new Dictionary<string, object?>
            {
                ["year"]            = r.Year,
                ["month"]           = r.Month,
                ["yearMonth"]       = $"{r.Year:D4}-{r.Month:D2}",
                ["departmentId"]    = r.DepartmentId,
                ["departmentName"]  = r.DepartmentName,
                ["totalRevenue"]    = r.TotalRevenue,
                ["totalCost"]       = r.TotalCost,
                ["netProfit"]       = net,
                ["patientCount"]    = r.PatientCount,
                ["avgRevenuePerPatient"] = avgRv,
            });
            if (rows.Count >= limit) break;
        }

        var single = string.Equals(query.Get("mode"), "single", StringComparison.OrdinalIgnoreCase) && rows.Count > 0
            ? rows[0]
            : null;

        return new FormPrefillResult(
            ContractCode: ContractCode,
            RowCount:     rows.Count,
            Rows:         rows)
        {
            Single = single
        };
    }
}
