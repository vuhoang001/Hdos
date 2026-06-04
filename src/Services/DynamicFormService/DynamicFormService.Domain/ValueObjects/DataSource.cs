namespace Hdos.DynamicFormService.Domain.ValueObjects;

// Khai báo nguồn dữ liệu ngoài mà một screen cần fetch khi render.
// Namespace    : key dùng trong expression, e.g. "patient"
// ServiceId    : tên service logic để frontend resolve base URL, e.g. "m01"
// ResourcePath : URL path với {param} placeholder, e.g. "/m01/patients/{patientId}"
// RequiredParams: params frontend phải cung cấp từ context hiện tại (route, query...)
public sealed record DataSource(
    string       Namespace,
    string       ServiceId,
    string       ResourcePath,
    List<string> RequiredParams);
