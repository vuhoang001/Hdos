using System.Net;
using System.Net.Http.Json;
using Hdos.LakehouseService.Application.Services;
using Hdos.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Infrastructure.ExternalClients;

public sealed class SourceProfileEnrollClient(
    HttpClient                              http,
    ILogger<SourceProfileEnrollClient>      logger)
    : ISourceProfileEnrollClient
{
    public async Task<Result> EnrollAsync(SourceProfileEnrollRequest req, CancellationToken ct)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/dm/sources", new
            {
                sourceSystem     = req.SourceSystem,
                recordType       = req.RecordType,
                displayName      = req.DisplayName,
                businessKeyField = req.BusinessKeyField,
                mappings         = req.Mappings,
            }, ct);

            if (resp.IsSuccessStatusCode) return Result.Success();

            // 409 (đã tồn tại) coi như OK — admin có thể đã đăng ký SourceProfile manual trước
            if (resp.StatusCode == HttpStatusCode.Conflict)
            {
                logger.LogInformation(
                    "SourceProfile {Src}/{Type} đã tồn tại bên DataMatching — skip enroll",
                    req.SourceSystem, req.RecordType);
                return Result.Success();
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "DataMatching enroll {Src}/{Type} failed: HTTP {Status} — {Body}",
                req.SourceSystem, req.RecordType, (int)resp.StatusCode, body);

            return Result.Failure(Error.Validation(
                $"DataMatching enroll fail ({(int)resp.StatusCode}): {body}"));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex,
                "Network error khi enroll SourceProfile {Src}/{Type}",
                req.SourceSystem, req.RecordType);
            return Result.Failure(Error.Validation(
                "Không kết nối được DataMatchingService — thử lại sau."));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "Timeout khi enroll SourceProfile {Src}/{Type}",
                req.SourceSystem, req.RecordType);
            return Result.Failure(Error.Validation(
                "Timeout khi gọi DataMatchingService."));
        }
    }
}
