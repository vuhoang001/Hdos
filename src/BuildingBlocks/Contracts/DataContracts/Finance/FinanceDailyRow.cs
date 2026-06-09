namespace Hdos.Contracts.DataContracts.Finance;

/// <summary>
/// Canonical schema cho 1 row tài chính theo ngày × khoa × loại hóa đơn.
/// Bất kỳ source nào (raw SQL Lakehouse, view DB, code-generated, RabbitMQ event, API ngoài)
/// muốn feed cho chart/form/export tài chính daily phải emit data theo schema này.
///
/// Aggregate (totals, per-dept, per-bucket) là việc của Consumer — KHÔNG bake vào source.
/// </summary>
public sealed record FinanceDailyRow(
    DateOnly InvoiceDate,
    int      DepartmentId,
    string   DepartmentName,
    decimal  TotalInvoiceAmount,
    decimal  TotalDiscountAmount,
    int      InvoiceCount,
    int      DistinctEncounterCount,
    string?  FinanceBucket);
