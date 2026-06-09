using System.Runtime.CompilerServices;
using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.Finance;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.DataContracts.Sources;

/// <summary>
/// Source SQL: query thẳng raw tables lakehouse PG, GROUP BY (dept, bucket) → 1 row per group.
/// Aggregate totals/perDept/perBucket là việc của Consumer — source chỉ emit canonical rows.
///
/// Filters hỗ trợ:
///   - <c>date</c>: yyyy-MM-dd. Default = UTC today.
///   - <c>department</c>: int. Optional — filter 1 khoa.
///
/// ⚠️ TODO_TABLE / TODO_COLUMN — chỉnh khi schema raw thật của bạn khác:
///   raw.invoices.invoice_date, .department_id, .total_amount, .discount_amount, .invoice_type
///   master.departments.id, .department_name
///   raw.encounters.encounter_id, .department_id, .encounter_date
/// </summary>
public sealed class FinanceDailySqlSource : IDataSource<FinanceDailyRow>
{
    public string ContractCode => FinanceDailyContract.ContractCode;
    public string SourceCode   => "sql";

    private const string InvoiceTable    = "raw.invoices";
    private const string DepartmentTable = "master.departments";
    private const string EncounterTable  = "raw.encounters";

    private readonly NpgsqlDataSource _ds;

    public FinanceDailySqlSource(NpgsqlDataSource ds) { _ds = ds; }

    public async IAsyncEnumerable<FinanceDailyRow> ReadAsync(
        DataContractQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var date     = query.GetDate("date") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var deptId   = query.GetInt("department");

        var sql = $"""
            SELECT
                i.invoice_date,
                i.department_id,
                COALESCE(d.department_name, 'Khoa #' || i.department_id)  AS department_name,
                COALESCE(SUM(i.total_amount), 0)::numeric                  AS total_invoice_amount,
                COALESCE(SUM(i.discount_amount), 0)::numeric               AS total_discount_amount,
                COUNT(i.id)::int                                            AS invoice_count,
                COUNT(DISTINCT e.encounter_id)::int                         AS distinct_encounter_count,
                COALESCE(i.invoice_type, '(không có)')                      AS finance_bucket
            FROM {InvoiceTable} i
            LEFT JOIN {DepartmentTable} d ON d.id = i.department_id
            LEFT JOIN {EncounterTable}  e ON e.department_id = i.department_id
                                          AND e.encounter_date = i.invoice_date
            WHERE i.invoice_date = @d
              AND (@dept IS NULL OR i.department_id = @dept)
            GROUP BY i.invoice_date, i.department_id, d.department_name, i.invoice_type
            ORDER BY total_invoice_amount DESC NULLS LAST
        """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@d",    date);
        cmd.Parameters.AddWithValue("@dept", (object?)deptId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            yield return new FinanceDailyRow(
                InvoiceDate:            reader.GetFieldValue<DateOnly>(0),
                DepartmentId:           reader.GetInt32(1),
                DepartmentName:         reader.GetString(2),
                TotalInvoiceAmount:     reader.GetDecimal(3),
                TotalDiscountAmount:    reader.GetDecimal(4),
                InvoiceCount:           reader.GetInt32(5),
                DistinctEncounterCount: reader.GetInt32(6),
                FinanceBucket:          reader.IsDBNull(7) ? null : reader.GetString(7));
        }
    }
}
