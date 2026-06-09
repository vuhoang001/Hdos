using Hdos.LakehouseService.Application.Charts.Sdui;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.Charts;

/// <summary>
/// 1 chart trực tiếp truy vấn lakehouse PG — KHÔNG đi qua DataMatching StagingRecord.
/// Mỗi config = 1 endpoint <c>GET /lakehouse/charts/{Code}</c>.
///
/// Implementor tự viết raw SQL với <see cref="NpgsqlDataSource"/> truyền vào.
/// Filter động (department, date, ...) lấy từ <see cref="IQueryCollection"/>.
/// Trả về <see cref="SduiPage"/> cùng JSON shape với /dm/pages/{code} — FE reuse renderer.
/// </summary>
[Obsolete("Use DataContract layer (doc 53): define IDataContract<TSchema> + IDataSource<TSchema> + IDataConsumer<TSchema, SduiPage>. Endpoint cũ /lakehouse/charts/{code} vẫn hoạt động nhưng sẽ được xóa sau khi prod stable.", false)]
public interface ILakehouseChartConfig
{
    /// <summary>Chart code dùng trong URL. VD "bed-occupancy".</summary>
    string Code { get; }

    /// <summary>Build trang. Tự query SQL + map sang SduiPage.</summary>
    Task<SduiPage> BuildAsync(
        NpgsqlDataSource  dataSource,
        DateOnly          reportDate,
        IQueryCollection  query,
        CancellationToken ct);
}
