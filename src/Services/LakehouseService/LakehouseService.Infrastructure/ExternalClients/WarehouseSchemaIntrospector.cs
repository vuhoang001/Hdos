using Hdos.LakehouseService.Application.Services;
using Npgsql;

namespace Hdos.LakehouseService.Infrastructure.ExternalClients;

public sealed class WarehouseSchemaIntrospector(NpgsqlDataSource warehouseDs)
    : IWarehouseSchemaIntrospector
{
    private const string Sql = """
        SELECT column_name, data_type, is_nullable
        FROM information_schema.columns
        WHERE table_schema = @schema AND table_name = @table
        ORDER BY ordinal_position
        """;

    public async Task<List<ColumnMetadata>> GetColumnsAsync(string viewName, CancellationToken ct)
    {
        var parts = viewName.Split('.');
        if (parts.Length != 2) return [];

        var schema = parts[0];
        var table  = parts[1];

        await using var conn = await warehouseDs.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table",  table);

        var result = new List<ColumnMetadata>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ColumnMetadata(
                Name:     reader.GetString(0),
                DataType: reader.GetString(1),
                Nullable: reader.GetString(2) == "YES"));
        }

        return result;
    }
}
