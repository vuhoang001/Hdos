using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.FormPrefill;
using Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Consumers;

// Stream PatientDailyNewRow → flat dict cho FormPrefill (FE bind expression
// {{sources.patient.rows[*].newPatientCount}} cho BarChart / Pie).
//
// ?mode=single → trả `single` object cho field bind (KPI / form field).
public sealed class PatientDailyNewFormPrefillConsumer
    : IDataConsumer<PatientDailyNewRow, FormPrefillResult>
{
    public string ContractCode => PatientDailyNewContract.ContractCode;
    public string ConsumerCode => "form-prefill";

    public async Task<FormPrefillResult> ConsumeAsync(
        IAsyncEnumerable<PatientDailyNewRow> stream,
        DataContractQuery                    query,
        CancellationToken                    ct)
    {
        var limit = query.GetInt("limit") ?? 50;
        var rows  = new List<IReadOnlyDictionary<string, object?>>();

        await foreach (var r in stream.WithCancellation(ct))
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["registerDate"]    = r.RegisterDate.ToString("yyyy-MM-dd"),
                ["departmentId"]    = r.DepartmentId,
                ["departmentName"]  = r.DepartmentName,
                ["newPatientCount"] = r.NewPatientCount,
                ["ageAvg"]          = r.AgeAvg,
                ["malePct"]         = r.MalePct,
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
