namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Finance;

// Schema 1 row tài chính tháng × khoa.
// Demo cho doc 59 — chứng minh Phase 4 auto-sync hoạt động: thêm contract mới
// không cần migration ở DynamicForm, dùng cùng Operation generic `lakehouse::prefill`
// + `lakehouse::chart` với param `contractCode=finance.monthly.row`.
public sealed record FinanceMonthlyRow(
    int     Year,
    int     Month,           // 1..12
    int     DepartmentId,
    string  DepartmentName,
    decimal TotalRevenue,
    decimal TotalCost,
    int     PatientCount);
