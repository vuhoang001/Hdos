using Hdos.SharedKernel;

namespace Hdos.LakehouseService.Application.Services;

/// <summary>
/// Adapter gọi DataMatchingService để đăng ký SourceProfile từ phía LakehouseService.
/// Idempotent: nếu profile đã tồn tại (HTTP 409), client trả <c>Result.Success</c> —
/// caller không phải xử lý case trùng.
///
/// Implementation: <c>SourceProfileEnrollClient</c> (HTTP qua <c>HttpClient</c> đăng ký
/// ở Infrastructure DI). Xem doc 45 §6.4.4.
/// </summary>
public interface ISourceProfileEnrollClient
{
    Task<Result> EnrollAsync(SourceProfileEnrollRequest req, CancellationToken ct);
}

public sealed record SourceProfileEnrollRequest(
    string                     SourceSystem,
    string                     RecordType,
    string                     DisplayName,
    string                     BusinessKeyField,
    Dictionary<string, string> Mappings);
