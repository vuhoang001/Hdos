# 07 — gRPC giữa các service

Hệ thống dùng gRPC cho **giao tiếp đồng bộ giữa các service** (server-to-server).
Hiện tại có 1 hợp đồng (`UserService`) do AuthService expose, OrderService gọi
khi tạo đơn.

> Nếu muốn gRPC cho **client → server**, có thể vẫn giữ kiến trúc này; chỉ
> khác là thêm một public proto và cấu hình ApiGateway để proxy gRPC (YARP có
> hỗ trợ — xem [09 — API Gateway](./09-api-gateway.md)).

## 1. Hợp đồng — `users.proto`

File: `src/BuildingBlocks/Contracts/Protos/users.proto`

```proto
syntax = "proto3";

option csharp_namespace = "Hdos.Contracts.Grpc.Users";

package hdos.users.v1;

import "google/protobuf/timestamp.proto";

service UserService {
  rpc GetUserById (GetUserByIdRequest) returns (UserReply);
  rpc UserExists  (UserExistsRequest)  returns (UserExistsReply);
}

message GetUserByIdRequest { string user_id = 1; }
message UserExistsRequest  { string user_id = 1; }
message UserExistsReply    { bool   exists  = 1; }

message UserReply {
  string id = 1;
  string email = 2;
  string full_name = 3;
  google.protobuf.Timestamp created_at_utc = 4;
}
```

Quy ước:

- `package hdos.<bounded-context>.v1` — version trong tên package, đổi major
  bằng `v2` thay vì sửa `v1` không tương thích.
- `option csharp_namespace = "Hdos.Contracts.Grpc.Users"` — namespace cho code
  C# generated.
- Tất cả Guid serialize dưới dạng `string` (tránh phụ thuộc kiểu UUID không
  chuẩn của proto).

## 2. Build pipeline

Project `Contracts.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Google.Protobuf" Version="3.27.3" />
  <PackageReference Include="Grpc.Net.Client" Version="2.65.0" />
  <PackageReference Include="Grpc.Tools" Version="2.65.0">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>
<ItemGroup>
  <Protobuf Include="Protos\users.proto" GrpcServices="Both" />
</ItemGroup>
```

`GrpcServices="Both"` ⇒ build sinh **server base class** + **client class**:

- `Hdos.Contracts.Grpc.Users.UserService.UserServiceBase` — abstract, server
  override để implement.
- `Hdos.Contracts.Grpc.Users.UserService.UserServiceClient` — concrete, client
  inject vào DI.

Cả 2 service Auth (server) và Order (client) đều `ProjectReference` tới
`Contracts` ⇒ hợp đồng tự động đồng bộ. Đổi proto một lần, cả hai bên đều
cần biên dịch lại — compiler sẽ chỉ ra điểm sai.

## 3. Server — AuthService

### 3.1 Cài

`AuthService.API.csproj`:

```xml
<PackageReference Include="Grpc.AspNetCore" Version="2.65.0" />
<ProjectReference Include="..\..\..\BuildingBlocks\Contracts\Contracts.csproj" />
```

### 3.2 Implementation

File: `src/Services/AuthService/AuthService.API/Grpc/UserGrpcService.cs`

```csharp
public sealed class UserGrpcService : UserService.UserServiceBase
{
    private readonly IUserRepository _users;
    private readonly ILogger<UserGrpcService> _logger;

    public UserGrpcService(IUserRepository users, ILogger<UserGrpcService> logger)
    {
        _users = users;
        _logger = logger;
    }

    public override async Task<UserReply> GetUserById(GetUserByIdRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id must be a GUID"));

        var user = await _users.GetByIdAsync(id, context.CancellationToken);
        if (user is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"User {id} not found"));

        return new UserReply
        {
            Id = user.Id.ToString(),
            Email = user.Email.Value,
            FullName = user.FullName,
            CreatedAtUtc = Timestamp.FromDateTime(DateTime.SpecifyKind(user.CreatedAtUtc, DateTimeKind.Utc))
        };
    }

    public override async Task<UserExistsReply> UserExists(UserExistsRequest request, ServerCallContext context) { ... }
}
```

Quy tắc lỗi:

- Input không hợp lệ → `StatusCode.InvalidArgument`.
- Không tìm thấy → `StatusCode.NotFound`.
- Lỗi không mong đợi → để framework chuyển thành `StatusCode.Internal`.

### 3.3 Listen 2 cổng

File: `src/Services/AuthService/AuthService.API/Program.cs`

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    var restPort = builder.Configuration.GetValue<int>("Kestrel:RestPort", 8080);
    var grpcPort = builder.Configuration.GetValue<int>("Kestrel:GrpcPort", 8081);
    options.ListenAnyIP(restPort, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
    options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();

var app = builder.Build();
app.MapControllers();
app.MapGrpcService<UserGrpcService>();
```

Vì sao 2 cổng?

- REST + Swagger UI cần HTTP/1.1.
- gRPC cần HTTP/2. Trên `http://` (không TLS), trình duyệt và HTTP/1.1 client
  không thể dùng cùng cổng với gRPC mà không gặp ALPN nightmare.
- Tách 2 cổng = đơn giản nhất. Production có TLS thì gộp một cổng được, cả
  client REST + gRPC đều ALPN happy.

## 4. Client — OrderService

### 4.1 Application port

File: `src/Services/OrderService/OrderService.Application/Abstractions/IUserLookupService.cs`

```csharp
public sealed record UserLookupDto(Guid Id, string Email, string FullName);

public interface IUserLookupService
{
    Task<Result<UserLookupDto>> GetByIdAsync(Guid userId, CancellationToken ct);
}
```

Application **không** import `Grpc.*`. Adapter ở Infrastructure mới biết.

### 4.2 Adapter

File: `src/Services/OrderService/OrderService.Infrastructure/Grpc/AuthUserLookupClient.cs`

```csharp
public sealed class AuthUserLookupClient : IUserLookupService
{
    private readonly UserService.UserServiceClient _client;
    private readonly ILogger<AuthUserLookupClient> _logger;

    public AuthUserLookupClient(UserService.UserServiceClient client, ILogger<AuthUserLookupClient> logger)
    { _client = client; _logger = logger; }

    public async Task<Result<UserLookupDto>> GetByIdAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var reply = await _client.GetUserByIdAsync(
                new GetUserByIdRequest { UserId = userId.ToString() },
                cancellationToken: ct);

            return new UserLookupDto(Guid.Parse(reply.Id), reply.Email, reply.FullName);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Result.Failure<UserLookupDto>(Error.NotFound("User"));
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC GetUserById failed: {Status}", ex.StatusCode);
            return Result.Failure<UserLookupDto>(
                new Error("User.GrpcError", $"AuthService gRPC error: {ex.Status.Detail}"));
        }
    }
}
```

Adapter **bọc kín** `RpcException` — Application chỉ thấy `Result<T>`.

### 4.3 Đăng ký DI

File: `src/Services/OrderService/OrderService.Infrastructure/DependencyInjection.cs`

```csharp
var authGrpcUrl = configuration["Services:Auth:GrpcUrl"] ?? "http://localhost:5111";
services.AddGrpcClient<UserService.UserServiceClient>(o => { o.Address = new Uri(authGrpcUrl); });
services.AddScoped<IUserLookupService, AuthUserLookupClient>();
```

`AddGrpcClient<T>` (gói `Grpc.Net.ClientFactory`) cấu hình `HttpClientFactory`
ngầm. Nếu cần retry/circuit breaker dùng Polly cũng add ở đây.

### 4.4 Cờ HTTP/2 plaintext

File: `OrderService.API/Program.cs` — dòng đầu tiên trước cả `WebApplication.CreateBuilder`:

```csharp
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
```

Mặc định .NET không cho HTTP/2 trên `http://`. Cờ này chỉ dùng cho dev/internal
cluster. Khi triển khai prod nên dùng `https://` để bỏ cờ.

## 5. Test thử

### Local (không Docker)

```bash
# Terminal 1: Auth (REST 5101 + gRPC 5111)
dotnet run --project src/Services/AuthService/AuthService.API

# Terminal 2: Order (REST 5102, client gRPC tới 5111)
dotnet run --project src/Services/OrderService/OrderService.API

# Tạo user trước
USER_ID=$(curl -s -X POST http://localhost:5101/auth/register \
  -H 'content-type: application/json' \
  -d '{"email":"a@b.io","fullName":"Alice","password":"secret123"}' | jq -r '.data.id')

# Tạo order — backend sẽ gRPC sang Auth verify trước
curl -X POST http://localhost:5102/orders \
  -H 'content-type: application/json' \
  -d "{\"customerId\":\"$USER_ID\",\"items\":[{\"productName\":\"Book\",\"quantity\":1,\"unitPrice\":12,\"currency\":\"USD\"}]}"
```

Nếu đổi `customerId` thành Guid bịa → response sẽ là 400 với
`{ "errorCode": "NotFound", "errorMessage": "User was not found" }` — đúng là
gRPC `NotFound` đã được adapter chuyển thành `Result.Failure(Error.NotFound)`.

### Docker

`docker compose up --build`. Compose đã set `Services__Auth__GrpcUrl=http://authservice:8081`.

### Bằng grpcurl (debug server)

```bash
# Ở server có proto reflection (chưa bật mặc định) thì grpcurl tiện hơn.
# Nếu chưa bật reflection, dùng -import-path / -proto:
grpcurl -plaintext -import-path src/BuildingBlocks/Contracts/Protos -proto users.proto \
  -d '{"user_id": "<guid>"}' \
  localhost:5111 hdos.users.v1.UserService/GetUserById
```

Muốn bật reflection (hữu ích khi debug):

```csharp
// AuthService.API.csproj
<PackageReference Include="Grpc.AspNetCore.Server.Reflection" Version="2.65.0" />

// Program.cs
builder.Services.AddGrpcReflection();
app.MapGrpcReflectionService();   // thường chỉ Development
```

## 6. Khi nào không nên dùng gRPC

- **Trình duyệt gọi trực tiếp** — gRPC-Web cần proxy. REST/JSON đơn giản hơn.
- **Event broadcast một-nhiều** — đó là việc của RabbitMQ, không phải gRPC.
- **Khi consumer down phải trữ message** — gRPC fail luôn, message bus mới
  buffer được.

Còn lại, nội bộ service-to-service: gRPC mặc định.
