namespace Hdos.DynamicFormService.Domain.ValueObjects;

// Khai báo nguồn dữ liệu ngoài mà một screen cần fetch khi render.
// Có 2 mode:
//
// 1. MANAGED — chỉ cần OperationId (combined "providerCode::operationKey").
//    GetScreenLayout resolve qua Provider/Operation catalog → trả ra ServiceId,
//    BaseUrl, ResourcePath, SchemaPath, RequiredParams, Kind đầy đủ cho FE.
//    Đổi URL ở Provider/Operation → tất cả screen tự cập nhật.
//
// 2. LEGACY — gõ tay ServiceId + ResourcePath (+ SchemaPath optional).
//    Giữ lại cho backward compat với DataSource đã tạo trước khi có catalog.
//
// Namespace     : key dùng trong DataBinding expression, e.g. "patient"
// RequiredParams: tên các {param} placeholder trong ResourcePath (hoặc Operation.Pattern)
// DefaultParams : giá trị mặc định cho {param} placeholder (key = param name).
//                 FE merge với URL params trước khi fetch — URL wins.
public sealed record DataSource(
    string       Namespace,
    string?      ServiceId,
    string?      ResourcePath,
    List<string> RequiredParams,
    string?      SchemaPath     = null,
    string?      OperationId    = null,
    Dictionary<string, string>? DefaultParams = null);
