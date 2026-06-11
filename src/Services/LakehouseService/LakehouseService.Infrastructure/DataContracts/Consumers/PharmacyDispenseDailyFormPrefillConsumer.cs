using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.FormPrefill;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Pharmacy;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

/// <summary>
/// Consumer "form-prefill": stream <see cref="PharmacyDispenseDailyRow"/> → flat dict
/// shape cho DynamicForm bind vào field.
///
/// Endpoint: <c>GET /lakehouse/contracts/pharmacy.dispense.daily.row/prefill</c>
///   ?mode=single → trả thêm <see cref="FormPrefillResult.Single"/> phẳng (form bind 1 row)
///   ?limit=N     → cap số row (default 50)
/// </summary>
public sealed class PharmacyDispenseDailyFormPrefillConsumer
    : IDataConsumer<PharmacyDispenseDailyRow, FormPrefillResult>
{
    public string ContractCode => PharmacyDispenseDailyContract.ContractCode;
    public string ConsumerCode => "form-prefill";

    public async Task<FormPrefillResult> ConsumeAsync(
        IAsyncEnumerable<PharmacyDispenseDailyRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        var limit = query.GetInt("limit") ?? 50;
        var rows  = new List<IReadOnlyDictionary<string, object?>>();

        await foreach (var r in stream.WithCancellation(ct))
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["dispenseDate"]        = r.DispenseDate.ToString("yyyy-MM-dd"),
                ["departmentId"]        = r.DepartmentId,
                ["departmentName"]      = r.DepartmentName,
                ["drugGroup"]           = r.DrugGroup,
                ["prescriptionCount"]   = r.PrescriptionCount,
                ["doseCount"]           = r.DoseCount,
                ["totalAmount"]         = r.TotalAmount,
                ["patientServedCount"]  = r.PatientServedCount,
                ["stockAlertLevel"]     = r.StockAlertLevel,
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
