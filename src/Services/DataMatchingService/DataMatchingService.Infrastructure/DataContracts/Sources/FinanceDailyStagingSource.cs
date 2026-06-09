using System.Runtime.CompilerServices;
using System.Text.Json;
using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.Finance;
using Hdos.DataMatchingService.Domain.Repositories;

namespace Hdos.DataMatchingService.Infrastructure.DataContracts.Sources;

/// <summary>
/// Source "staging": đọc <c>StagingRecord.CanonicalPayload</c> đã qua SourceProfile mapping
/// → parse JSON → emit <see cref="FinanceDailyRow"/>.
///
/// Đây là Path A (doc 49) re-architected: thay vì SduiEngine fetch + decode trong BuildPage,
/// source này emit canonical row → consumer ở Lakehouse (qua HTTP) hoặc DataMatching tự build.
///
/// Filter:
///   - <c>sourceSystem</c>: filter theo source system (default tất cả)
/// </summary>
public sealed class FinanceDailyStagingSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "staging";

    public const string DefaultSourceSystem = "lakehouse:finance_daily";
    public const string RecordType          = "finance-daily";

    private readonly IStagingRecordRepository _records;

    public FinanceDailyStagingSource(IStagingRecordRepository records) { _records = records; }

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sourceSystem = query.Get("sourceSystem");

        var staging = await _records.GetMatchedAsync(
            sourceSystem: sourceSystem,
            recordType:   RecordType,
            from:         null,
            to:           null,
            ct:           ct);

        foreach (var rec in staging)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(rec.CanonicalPayload)) continue;

            FinanceDailyRow? row;
            try { row = ParseRow(rec.CanonicalPayload); }
            catch { continue; }

            if (row is not null) yield return row;
        }
    }

    private static FinanceDailyRow? ParseRow(string canonicalPayload)
    {
        using var doc = JsonDocument.Parse(canonicalPayload);
        var el = doc.RootElement;
        if (el.ValueKind != JsonValueKind.Object) return null;

        return new FinanceDailyRow(
            InvoiceDate:            ParseDate(el, "InvoiceDate")    ?? DateOnly.FromDateTime(DateTime.UtcNow),
            DepartmentId:           ParseInt(el,  "DepartmentId"),
            DepartmentName:         ParseStr(el,  "DepartmentName") ?? $"Khoa #{ParseInt(el, "DepartmentId")}",
            TotalInvoiceAmount:     ParseDec(el,  "TotalInvoiceAmount"),
            TotalDiscountAmount:    ParseDec(el,  "TotalDiscountAmount"),
            InvoiceCount:           ParseInt(el,  "InvoiceCount"),
            DistinctEncounterCount: ParseInt(el,  "DistinctEncounterCount"),
            FinanceBucket:          ParseStr(el,  "FinanceBucket"));
    }

    private static string? ParseStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int ParseInt(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static decimal ParseDec(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0;

    private static DateOnly? ParseDate(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(v.GetString(), out var d) ? d : null;
}
