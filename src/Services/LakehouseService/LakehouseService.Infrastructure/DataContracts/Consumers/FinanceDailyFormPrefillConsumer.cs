using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.FormPrefill;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

/// <summary>
/// Consumer "form-prefill": stream <see cref="FinanceDailyRow"/> → flat dict shape FE map vào form field.
///
/// Cross-service: DynamicForm gọi endpoint <c>GET /lakehouse/contracts/finance.daily.row/prefill</c>
/// (qua Provider/Operation catalog của doc 41/52) → nhận <see cref="FormPrefillResult"/> →
/// FE bind từng key vào field tương ứng trong screen designer.
///
/// Filter <c>?limit=N</c> để giới hạn số row trả về (form thường chỉ cần 1-vài row).
/// </summary>
public sealed class FinanceDailyFormPrefillConsumer
    : IDataConsumer<FinanceDailyRow, FormPrefillResult>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string ConsumerCode => "form-prefill";

    public async Task<FormPrefillResult> ConsumeAsync(
        IAsyncEnumerable<FinanceDailyRow> stream,
        DataContractQuery query,
        CancellationToken ct)
    {
        var limit = query.GetInt("limit") ?? 50;
        var rows  = new List<IReadOnlyDictionary<string, object?>>();

        await foreach (var r in stream.WithCancellation(ct))
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["invoiceDate"]             = r.InvoiceDate.ToString("yyyy-MM-dd"),
                ["departmentId"]            = r.DepartmentId,
                ["departmentName"]          = r.DepartmentName,
                ["totalInvoiceAmount"]      = r.TotalInvoiceAmount,
                ["totalDiscountAmount"]     = r.TotalDiscountAmount,
                ["invoiceCount"]            = r.InvoiceCount,
                ["distinctEncounterCount"]  = r.DistinctEncounterCount,
                ["financeBucket"]           = r.FinanceBucket,
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
