using Hdos.Common.Responses;
using Hdos.Contracts.DataContracts;
using Hdos.Contracts.DataContracts.FormPrefill;
using Hdos.LakehouseService.Application.Charts.Sdui;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hdos.LakehouseService.API.Controllers;

/// <summary>
/// Endpoint mới đi qua <see cref="DataContractGateway"/> (doc 53). Chạy song song với
/// <c>/lakehouse/charts/{code}</c> cũ — endpoint cũ KHÔNG bị xóa, chỉ deprecate ở P6.
///
/// Khác biệt vs endpoint cũ:
///   • URL dùng full contract code (<c>finance.daily.row</c>) thay vì chart code (<c>finance-daily</c>)
///   • Hỗ trợ <c>?source=sql|demo|...</c> swap data source runtime
///   • Hỗ trợ <c>?consumer=chart|csv|...</c> swap output format (nếu có consumer khác đăng ký)
///   • Discovery: <c>GET /lakehouse/contracts</c> list contract + source + consumer registered
/// </summary>
[ApiController]
[Route("lakehouse/contracts")]
[Tags("Lakehouse Data Contracts — Universal funnel (doc 53)")]
public sealed class DataContractChartController(DataContractGateway gateway) : ControllerBase
{
    /// <summary>List tất cả data contract đã đăng ký + schema type.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DataContractMetadata>>), StatusCodes.Status200OK)]
    public IActionResult List()
    {
        var items = gateway.AvailableContracts
            .Select(c => new DataContractMetadata(c.Code, c.DisplayName, c.SchemaType.Name))
            .OrderBy(x => x.Code)
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<DataContractMetadata>>.Ok(items));
    }

    /// <summary>
    /// Render chart cho contract qua consumer "chart" (default).
    /// Toàn bộ querystring (date, department, ...) sẽ pass nguyên xi vào <see cref="DataContractQuery.Filters"/>.
    /// </summary>
    /// <param name="code">Contract code, VD <c>finance.daily.row</c>.</param>
    /// <param name="sourceCode">(Optional) Source: <c>sql</c>, <c>demo</c>,... Default = source đầu tiên đăng ký.</param>
    /// <param name="consumerCode">(Optional) Consumer: <c>chart</c> mặc định. Đăng ký consumer khác (csv, json) để swap output.</param>
    [HttpGet("{code}/chart")]
    [ProducesResponseType(typeof(ApiResponse<SduiPage>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChart(
        string code,
        [FromQuery(Name = "source")] string? sourceCode,
        [FromQuery(Name = "consumer")] string consumerCode = "chart",
        CancellationToken ct = default)
    {
        IDataContract contract;
        try { contract = gateway.Require(code); }
        catch (DataContractNotFoundException)
        {
            return NotFound(ApiResponse<SduiPage>.Fail(
                "CONTRACT.NOT_FOUND",
                $"Contract '{code}' chưa đăng ký. Xem GET /lakehouse/contracts."));
        }

        var query = BuildQuery();

        try
        {
            // Reflection dispatch: chọn TSchema theo metadata, output luôn SduiPage cho endpoint này.
            var method = typeof(DataContractGateway)
                .GetMethod(nameof(DataContractGateway.ConsumeAsync))!
                .MakeGenericMethod(contract.SchemaType, typeof(SduiPage));

            var taskObj = method.Invoke(gateway, [code, consumerCode, sourceCode, query, ct])!;
            var page    = await (Task<SduiPage>)taskObj;

            return Ok(ApiResponse<SduiPage>.Ok(page));
        }
        catch (DataConsumerNotFoundException ex)
        {
            return NotFound(ApiResponse<SduiPage>.Fail(
                "CONSUMER.NOT_FOUND",
                $"{ex.Message} Đăng ký consumer trả về SduiPage để dùng endpoint /chart."));
        }
        catch (DataSourceNotFoundException ex)
        {
            return NotFound(ApiResponse<SduiPage>.Fail("SOURCE.NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Form pre-fill cho DynamicForm — DynamicForm gọi qua Provider/Operation catalog (doc 41/52).
    /// Consumer "form-prefill" map row contract → flat dict shape FE bind vào form field.
    /// </summary>
    /// <param name="code">Contract code, VD <c>finance.daily.row</c>.</param>
    /// <param name="sourceCode">(Optional) Source: <c>sql</c>, <c>demo</c>...</param>
    [HttpGet("{code}/prefill")]
    [ProducesResponseType(typeof(ApiResponse<FormPrefillResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrefill(
        string code,
        [FromQuery(Name = "source")] string? sourceCode,
        CancellationToken ct = default)
    {
        IDataContract contract;
        try { contract = gateway.Require(code); }
        catch (DataContractNotFoundException)
        {
            return NotFound(ApiResponse<FormPrefillResult>.Fail(
                "CONTRACT.NOT_FOUND",
                $"Contract '{code}' chưa đăng ký."));
        }

        var query = BuildQuery();

        try
        {
            var method = typeof(DataContractGateway)
                .GetMethod(nameof(DataContractGateway.ConsumeAsync))!
                .MakeGenericMethod(contract.SchemaType, typeof(FormPrefillResult));

            var taskObj = method.Invoke(gateway, [code, "form-prefill", sourceCode, query, ct])!;
            var result  = await (Task<FormPrefillResult>)taskObj;

            return Ok(ApiResponse<FormPrefillResult>.Ok(result));
        }
        catch (DataConsumerNotFoundException ex)
        {
            return NotFound(ApiResponse<FormPrefillResult>.Fail(
                "CONSUMER.NOT_FOUND",
                $"{ex.Message} Đăng ký consumer 'form-prefill' trả về FormPrefillResult."));
        }
        catch (DataSourceNotFoundException ex)
        {
            return NotFound(ApiResponse<FormPrefillResult>.Fail("SOURCE.NOT_FOUND", ex.Message));
        }
    }

    private DataContractQuery BuildQuery()
    {
        var pairs = HttpContext.Request.Query
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v)));
        return DataContractQuery.From(pairs);
    }

    public sealed record DataContractMetadata(string Code, string DisplayName, string SchemaTypeName);
}
