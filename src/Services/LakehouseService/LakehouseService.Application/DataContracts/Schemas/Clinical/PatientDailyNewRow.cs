namespace Hdos.LakehouseService.Application.DataContracts.Schemas.Clinical;

/// <summary>
/// 1 row số bệnh nhân ĐĂNG KÝ MỚI theo (ngày × khoa).
/// Service-local — LakehouseService owns (data từ patient registration funnel).
///
/// AgeAvg = trung bình tuổi của bệnh nhân mới trong nhóm đó.
/// MalePct = tỉ lệ % nam giới (0-100).
///
/// Aggregate (totals, top dept, age distribution) là việc của Consumer, KHÔNG bake vào source.
/// </summary>
public sealed record PatientDailyNewRow(
    DateOnly RegisterDate,
    int      DepartmentId,
    string   DepartmentName,
    int      NewPatientCount,
    double   AgeAvg,
    double   MalePct);
