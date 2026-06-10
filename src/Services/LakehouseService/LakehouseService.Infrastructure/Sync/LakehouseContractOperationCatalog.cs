namespace Hdos.LakehouseService.Infrastructure.Sync;

// Catalog các Operation mà LakehouseService expose qua Provider catalog của
// DynamicForm. Operation generic (pattern có `{contractCode}` template) — admin
// truyền contractCode khi tạo DataSource. Một Operation phục vụ TẤT CẢ contract,
// không cần per-contract entry. Đó là design hiện tại (migration seed lakehouse).
//
// Singleton, populate trong DataContractsRegistration.AddLakehouseDataContracts.
// LakehouseRegistrySyncHostedService đọc khi startup, build payload SyncRegistry
// gửi sang DynamicForm.
public sealed class LakehouseContractOperationCatalog
{
    private readonly List<OperationEntry> _entries = new();

    public IReadOnlyList<OperationEntry> Entries => _entries;

    public LakehouseContractOperationCatalog AddOperation(OperationEntry entry)
    {
        _entries.Add(entry);
        return this;
    }
}

public sealed record OperationEntry(
    string   OperationKey,
    string   DisplayName,
    string   Pattern,
    string?  SchemaPath,
    string[] RequiredParams,
    string   Kind);
