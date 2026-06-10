# 59 — Auto-sync DataContract Registry (Phase 4)

> **Tier 1 → Tier 2 tự động hoá**: Lakehouse startup → gRPC sang DynamicForm → upsert Provider `lakehouse` + 2 Operation. Hết phải viết migration `HasData()` mỗi lần thêm contract / đổi `BaseUrl`.
>
> Companion: doc 58 (định nghĩa hai tier), doc 41 (Loose Coupling — Provider/Operation catalog).

---

## 1. Bài toán doc 58 để lại

Doc 58 cho `lakehouse` Provider + 2 Operation vào DynamicFormDb bằng `HasData()` ở `OnModelCreating`. Nó **chạy được** nhưng có 3 mùi:

| Mùi | Hệ quả |
|-----|--------|
| Mỗi khi `BaseUrl` Lakehouse đổi (rename hostname Docker, deploy multi-region) → admin DynamicForm phải sửa migration | Không gọi là loose coupling nữa |
| Thêm Operation mới (`export-csv`, `schema`...) → migration mới ở DynamicForm, dù logic nằm ở Lakehouse | Sai DDD: catalog rò rỉ implementation detail của service khác |
| Hai service biên dịch độc lập, nhưng config Provider lại bị "đóng băng" tại snapshot model của DynamicForm | Drift: code Lakehouse thay đổi mà catalog không phản ánh |

Phase 4 chuyển catalog từ "tĩnh, do DynamicForm khai báo" → "động, do service nguồn (Lakehouse) tự push lên".

---

## 2. Kiến trúc

```
┌────────────── LakehouseService ──────────────┐         ┌──────── DynamicFormService ─────────┐
│                                              │         │                                     │
│  LakehouseContractOperationCatalog (Singleton)│         │  DataContractRegistrySyncGrpcService│
│    ├─ prefill  /lakehouse/contracts/{c}/...  │         │    │                                │
│    └─ chart    /lakehouse/contracts/{c}/...  │         │    ▼                                │
│                                              │ gRPC    │  SyncProviderRegistryCommand (MediatR) │
│  LakehouseRegistrySyncHostedService          │ ──────► │    │                                │
│    Build SyncRegistryRequest                 │  8081   │    ▼ upsert idempotent              │
│    Retry 30 × 5s                             │ HTTP/2  │  IProviderRepository / IOperationRepo│
│                                              │         │    │                                │
└──────────────────────────────────────────────┘         │    ▼                                │
                                                         │  DynamicFormDb (Postgres)            │
                                                         │    Providers / Operations            │
                                                         └─────────────────────────────────────┘
```

**Lifecycle**:
1. `docker compose up -d` → cả 2 service start cùng lúc. Lakehouse `depends_on: dynamicformservice` (started) để tránh first attempt chắc chắn fail.
2. Lakehouse build `SyncRegistryRequest` từ catalog → gọi `SyncRegistry`.
3. DynamicForm dispatch sang `SyncProviderRegistryCommand`:
   - Provider `lakehouse` chưa có → `Provider.Create()`; đã có → `Update(displayName, baseUrl)` + `Activate()`.
   - Mỗi Operation trong payload: chưa có → `Operation.Create()`; đã có → `Update(...)`.
   - Operations DB có nhưng payload không có → `Deactivate()` (không xóa cứng, giữ audit).
4. `SaveChanges` 1 lần ở cuối. Idempotent — gọi 100 lần đều ra cùng state.

---

## 3. Files thay đổi

### Tạo mới

| File | Vai trò |
|------|---------|
| `src/BuildingBlocks/Contracts/Protos/datacontract_registry.proto` | Service `DataContractRegistrySyncService`, rpc `SyncRegistry` |
| `src/Services/DynamicFormService/DynamicFormService.Application/Features/Providers/SyncRegistry/SyncProviderRegistryCommand.cs` | Command + Validator + Handler (upsert idempotent + deactivate drift) |
| `src/Services/DynamicFormService/DynamicFormService.API/Grpc/DataContractRegistrySyncGrpcService.cs` | Adapter mỏng: gRPC → MediatR |
| `src/Services/LakehouseService/LakehouseService.Infrastructure/Sync/LakehouseContractOperationCatalog.cs` | Singleton, populate trong `AddLakehouseDataContracts` |
| `src/Services/LakehouseService/LakehouseService.Infrastructure/Sync/LakehouseRegistrySyncHostedService.cs` | `IHostedService` chạy nền lúc startup, retry 30 × 5s |
| `src/Services/DynamicFormService/DynamicFormService.Infrastructure/Persistence/Migrations/20260610024839_RemoveLakehouseSeed.cs` | Xóa 3 row tĩnh (idempotent) |
| `docs/59-auto-sync-datacontract-registry.md` | Doc này |

### Sửa

| File | Thay đổi |
|------|----------|
| `src/BuildingBlocks/Contracts/Contracts.csproj` | Add `<Protobuf>` mới |
| `src/Services/DynamicFormService/DynamicFormService.API/DynamicFormService.API.csproj` | Add `Grpc.AspNetCore`, ref `Contracts` |
| `src/Services/DynamicFormService/DynamicFormService.API/Program.cs` | `ConfigureKestrel` 2 port (8080 REST + 8081 gRPC), `AddGrpc`, `MapGrpcService` |
| `src/Services/DynamicFormService/DynamicFormService.Infrastructure/Persistence/DynamicFormDbContext.cs` | Bỏ `SeedLakehouseProvider()` |
| `src/Services/LakehouseService/LakehouseService.Infrastructure/LakehouseService.Infrastructure.csproj` | Add `Grpc.Net.ClientFactory`, `Microsoft.Extensions.Hosting.Abstractions` |
| `src/Services/LakehouseService/LakehouseService.Infrastructure/DataContracts/Registration/DataContractsRegistration.cs` | Populate catalog với 2 generic operation |
| `src/Services/LakehouseService/LakehouseService.Infrastructure/DependencyInjection.cs` | `AddGrpcClient` + `AddHostedService` |
| `docker-compose.yml` | Env `Services__DynamicForm__GrpcUrl`, `Services__Lakehouse__PublicBaseUrl`, `depends_on: dynamicformservice` |
| `docs/58-lakehouse-dynamicform-integration.md` | Phase 4: ❌ → ✅ DONE |

---

## 4. Contract gRPC

```proto
service DataContractRegistrySyncService {
  rpc SyncRegistry (SyncRegistryRequest) returns (SyncRegistryReply);
}

message SyncRegistryRequest {
  ProviderSpec provider = 1;
  repeated OperationSpec operations = 2;
}

message ProviderSpec    { string code; string display_name; string base_url; }
message OperationSpec   {
  string operation_key; string display_name; string pattern;
  string schema_path;   // empty = null
  repeated string required_params;
  string kind;          // "Single" | "List"
}
message SyncRegistryReply {
  int32 upserted_provider_count;
  int32 upserted_operation_count;
  int32 deactivated_operation_count;
}
```

**Kind chỉ chấp nhận `"Single"` hoặc `"List"`** — match `OperationKind` enum. Charts cũng dùng `"Single"` (response là `SduiPage` object).

**Operation generic, không per-contract**: pattern có `{contractCode}` template → 1 Operation phục vụ tất cả contract Lakehouse có. Thêm contract mới (vd `inventory.daily.row`) → KHÔNG cần đụng catalog, KHÔNG cần migration. Phase 4 tự match.

---

## 5. Idempotency + Drift handling

```
Snapshot DB trước sync:
  Providers : []
  Operations: []

Sync lần 1 (payload: lakehouse + prefill + chart)
  → Provider.Create(lakehouse)
  → Operation.Create(prefill), Operation.Create(chart)
  → Reply: upsertedProvider=1, upsertedOps=2, deactivated=0

Sync lần 2 (cùng payload)
  → Provider.Update(... same values ...) + Activate
  → Operation.Update(prefill), Operation.Update(chart)
  → Reply: upsertedProvider=1, upsertedOps=2, deactivated=0   (DB không đổi state)

Sync lần 3 (Lakehouse xóa op `chart` khỏi catalog)
  → Provider.Update
  → Operation.Update(prefill)
  → Operation `chart` còn trong DB nhưng KHÔNG có trong payload → Deactivate()
  → Reply: upsertedProvider=1, upsertedOps=1, deactivated=1
```

**Không hard-delete** — Operation `chart` vẫn còn row, chỉ chuyển sang `Status=Inactive`. Lý do:
- FormScreen.DataSource có thể đang ref `lakehouse::chart`. Xóa cứng → orphan reference.
- `GetScreenLayoutQuery` đã handle case Operation Inactive → trả `baseUrl=null`. FE thấy → biết "data source này tạm offline". Admin có thể "rollback" bằng cách add lại op vào catalog Lakehouse → restart → sync re-activate.

---

## 6. Retry + Failure mode

`LakehouseRegistrySyncHostedService` chạy `Task.Run` trong `StartAsync` → KHÔNG block app startup. Vòng lặp:

```
for attempt in 1..30:
    try call SyncRegistry → success → return
    except (any) → log warning, sleep 5s
log error after 30 attempts → service vẫn chạy, chỉ là sync chưa thành công
```

Tổng deadline = 30 × 5s = 150s. Đủ cho DynamicForm restart hoặc DB migration đầu tiên.

**Nếu sync vĩnh viễn fail**:
- Lakehouse vẫn serve `/lakehouse/contracts/*` bình thường (sync chỉ đẩy metadata catalog, không liên quan runtime).
- DynamicForm `GetScreenLayoutQuery` trả `baseUrl=null` cho mọi DataSource ref `lakehouse::*`. FE phải fallback / skip.
- Fix: kiểm tra log Lakehouse `"Lakehouse → DynamicForm registry sync attempt X/30 failed"`. Thường do network/cấu hình URL sai.

---

## 7. Verify

```bash
# Khởi động (Lakehouse phải start sau DynamicForm)
docker compose up -d --build dynamicformservice lakehouseservice

# Đợi 10-15s rồi check
docker compose logs lakehouseservice 2>&1 | grep "registry sync"
# → "Lakehouse → DynamicForm registry sync OK: upsertedOps=2 deactivated=0 (attempt 1)"

# Check Provider qua REST admin
curl -s https://localhost:8443/forms/admin/providers \
  -H "Authorization: Bearer <token>" | jq '.[] | select(.code=="lakehouse")'
# → { code: "lakehouse", baseUrl: "http://lakehouseservice:8080", ... }

curl -s https://localhost:8443/forms/admin/providers/lakehouse/operations \
  -H "Authorization: Bearer <token>" | jq '[.[].operationKey]'
# → ["prefill", "chart"]
```

---

## 8. Thêm operation mới (vd `export-csv`)

```csharp
// LakehouseService.Infrastructure/DataContracts/Registration/DataContractsRegistration.cs
var catalog = new LakehouseContractOperationCatalog()
    .AddOperation(new OperationEntry("prefill", ..., "/lakehouse/contracts/{contractCode}/prefill", ...))
    .AddOperation(new OperationEntry("chart",   ..., "/lakehouse/contracts/{contractCode}/chart",   ...))
    .AddOperation(new OperationEntry(           // ← thêm dòng này
        OperationKey:   "export-csv",
        DisplayName:    "Lakehouse contract export CSV",
        Pattern:        "/lakehouse/contracts/{contractCode}/export.csv",
        SchemaPath:     null,
        RequiredParams: new[] { "contractCode" },
        Kind:           "Single"));
```

→ Build Lakehouse, `docker compose up -d --build lakehouseservice`. Đợi 5s. Op `lakehouse::export-csv` xuất hiện ở DynamicFormDb. **Zero migration**.

Xóa op tương tự — xóa dòng `.AddOperation(...)` → restart → op auto-Deactivate.

---

## 9. Tại sao không gộp luôn Phase 2 (schema discovery)

Phase 2 expose `/lakehouse/contracts/{code}/schema` reflection trên `IDataContract.SchemaType` → list field. Phase 4 chỉ đẩy **metadata catalog (Provider+Operation)**, không đẩy schema. Hai concern khác nhau:

- Phase 4: "Lakehouse có những endpoint kiểu nào".
- Phase 2: "Endpoint `prefill` của contract `finance.daily.row` trả những field gì".

Cả hai chạy độc lập. Khi Phase 2 implement, chỉ cần thêm endpoint REST mới ở Lakehouse — KHÔNG đụng auto-sync gRPC. Path `SchemaPath` trong Operation đã reserve sẵn `/lakehouse/contracts/{contractCode}/schema`.

---

## 10. Tương lai — service khác cũng dùng

`DataContractRegistrySyncService` không gắn chặt với Lakehouse — proto generic. M01Service, DataMatchingService hoặc bất kỳ service nào có DataContract đều có thể implement cùng pattern:

```csharp
// M01Service.Infrastructure/Sync/M01OperationCatalog.cs  (sao chép từ Lakehouse)
// M01Service.Infrastructure/Sync/M01RegistrySyncHostedService.cs  (gửi Provider code="m01")
```

Khi đó DynamicFormDb sẽ có nhiều provider: `lakehouse`, `m01`, `datamatch`... tất cả tự-quản lý.
