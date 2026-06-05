using System.Text.Json;
using Hdos.Common.Responses;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Features.Ingest;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Hdos.DataMatchingService.API.Controllers;

/// <summary>
/// Nạp dữ liệu thô từ hệ thống nguồn (HIS, BHYT, file...) vào staging. Sau khi nạp, MatchingWorker sẽ tự dedup và match (~30s).
/// </summary>
[ApiController]
[Route("dm/ingest")]
[Tags("1. Ingest — Nạp dữ liệu vào staging")]
// [Authorize]
public sealed class IngestController(ISender sender) : ControllerBase
{
    /// <summary>Nạp 1 record JSON (record-by-record).</summary>
    /// <remarks>
    /// Dùng cho integration realtime — mỗi lần gọi nạp 1 bản ghi.
    /// Cần đăng ký <c>SourceProfile</c> trước (POST <c>/dm/sources</c>) để biết cách map field.
    ///
    /// Ví dụ body:
    /// <code>
    /// {
    ///   "sourceSystem": "his-01",
    ///   "recordType":   "benh-nhan",
    ///   "payload":      { "ho_ten": "Nguyễn Văn A", "ma_benh_nhan": "BN001", ... },
    ///   "businessKeyOverride": null
    /// }
    /// </code>
    /// </remarks>
    /// <response code="202">Đã nhận, đang chờ MatchingWorker xử lý (~30s).</response>
    /// <response code="404">Chưa đăng ký SourceProfile cho cặp (sourceSystem, recordType).</response>
    /// <response code="409">Trùng business key (dedup SHA-256) — record đã tồn tại.</response>
    [HttpPost("json")]
    [ProducesResponseType(typeof(ApiResponse<IngestResultDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IngestJson(
        [FromBody] IngestJsonRequest body,
        CancellationToken ct)
    {
        var cmd = new IngestJsonCommand(body.SourceSystem, body.RecordType, body.Payload.GetRawText(), body.BusinessKeyOverride);
        var result = await sender.Send(cmd, ct);

        return result.IsSuccess
            ? Accepted(ApiResponse<IngestResultDto>.Ok(result.Value))
            : result.Error.Code == "Conflict"
                ? Conflict(ApiResponse<IngestResultDto>.Fail(result.Error.Code, result.Error.Message))
                : NotFound(ApiResponse<IngestResultDto>.Fail(result.Error.Code, result.Error.Message));
    }

    /// <summary>Nạp file batch (CSV / Excel / JSON Lines).</summary>
    /// <remarks>
    /// Upload multipart/form-data với 1 file ≤ 50MB. Mỗi dòng/object trong file = 1 record.
    /// Dùng cho migration ban đầu hoặc batch định kỳ.
    /// </remarks>
    /// <response code="202">Đã nhận file, trả về số record được tách + danh sách lỗi (nếu có).</response>
    /// <response code="400">File rỗng hoặc không upload.</response>
    /// <response code="404">Chưa đăng ký SourceProfile cho cặp (sourceSystem, recordType).</response>
    [HttpPost("file")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<IngestBatchResultDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestFile(
        IFormFile file,
        [FromForm] string sourceSystem,
        [FromForm] string recordType,
        [FromForm] string? businessKeyOverride,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("BadRequest", "No file uploaded."));

        using var stream = file.OpenReadStream();
        var cmd = new IngestFileCommand(sourceSystem, recordType, stream, file.FileName, businessKeyOverride);
        var result = await sender.Send(cmd, ct);

        return result.IsSuccess
            ? Accepted(ApiResponse<IngestBatchResultDto>.Ok(result.Value))
            : NotFound(ApiResponse<IngestBatchResultDto>.Fail(result.Error.Code, result.Error.Message));
    }
}

/// <summary>Payload nạp 1 record JSON.</summary>
/// <param name="SourceSystem">Mã hệ thống nguồn — phải khớp SourceProfile đã đăng ký. VD: <c>his-01</c>.</param>
/// <param name="RecordType">Loại bản ghi trong hệ thống đó. VD: <c>benh-nhan</c>, <c>chung-tu</c>.</param>
/// <param name="Payload">JSON raw của record (object). Field name theo schema nguồn — sẽ được map sang canonical.</param>
/// <param name="BusinessKeyOverride">Ghi đè business key thay vì lấy từ field cấu hình ở SourceProfile. Để <c>null</c> nếu không cần.</param>
public sealed record IngestJsonRequest(
    string SourceSystem,
    string RecordType,
    JsonElement Payload,
    string? BusinessKeyOverride);
