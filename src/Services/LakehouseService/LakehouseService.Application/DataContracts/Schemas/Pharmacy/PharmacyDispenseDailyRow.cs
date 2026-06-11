namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Pharmacy;

/// <summary>
/// 1 row phát thuốc theo (ngày × khoa × nhóm thuốc).
/// Service-local — LakehouseService owns (data từ pharmacy dispense funnel).
///
/// Granularity: 1 dept thường có 3-5 row (mỗi DrugGroup 1 row). Aggregate
/// (totals, top dept, pie distribution) là việc của Consumer.
///
/// StockAlertLevel = null | "low" | "out" — sinh AlertList trong chart.
/// </summary>
public sealed record PharmacyDispenseDailyRow(
    DateOnly DispenseDate,
    int      DepartmentId,
    string   DepartmentName,
    string   DrugGroup,
    int      PrescriptionCount,
    int      DoseCount,
    decimal  TotalAmount,
    int      PatientServedCount,
    string?  StockAlertLevel);
