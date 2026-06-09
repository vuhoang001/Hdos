namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

/// <summary>
/// Canonical schema cho 1 row tài chính theo ngày × khoa × loại hóa đơn.
/// Service-local schema — chỉ LakehouseService owns/exposes contract này.
/// Các service khác consume qua HTTP <c>/lakehouse/contracts/finance.daily.row/*</c>.
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
