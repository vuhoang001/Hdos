# EF Core Migrations

Hướng dẫn quản lý schema database cho 3 service (`AuthService`, `OrderService`,
`NotificationService`) bằng EF Core Migrations.

> **TL;DR** — Mỗi service tự gọi `Database.MigrateAsync()` lúc khởi động
> (xem `EnsureDatabaseAsync` trong từng `Program.cs`). Vì vậy bạn **chỉ cần
> commit migration vào source**, container sẽ tự apply khi `docker compose up`.
> Nếu chưa có migration nào, bảng sẽ KHÔNG được tạo và mọi query sẽ chết với
> lỗi `Invalid object name 'Users'` (208).

## 1. Yêu cầu một lần

```bash
# Cài tool dotnet-ef cùng major version với EF Core 8 trong project
dotnet tool install --global dotnet-ef --version "8.0.*"

# Đảm bảo PATH có ~/.dotnet/tools (thêm vào ~/.bashrc / ~/.zshrc)
export PATH="$PATH:$HOME/.dotnet/tools"

# Trên máy chỉ có .NET SDK 9/10 mà không có runtime 8 thì set thêm:
export DOTNET_ROOT="$HOME/.dotnet"
```

Mỗi project `*.API` đã được khai báo `Microsoft.EntityFrameworkCore.Design`
(tooling cần ở startup project). `MigrationsAssembly(...)` được wire trong
`*.Infrastructure/DependencyInjection.cs` nên migration sẽ nằm cạnh `DbContext`,
không lẫn vào layer API.

## 2. Tạo migration mới

Cú pháp chung:

```bash
dotnet ef migrations add <TenMigration> \
  --project        src/Services/<Service>/<Service>.Infrastructure \
  --startup-project src/Services/<Service>/<Service>.API \
  -o Persistence/Migrations
```

Cụ thể cho 3 service (chạy từ repo root):

```bash
# AuthService
dotnet ef migrations add Init \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API \
  -o Persistence/Migrations

# OrderService
dotnet ef migrations add Init \
  --project src/Services/OrderService/OrderService.Infrastructure \
  --startup-project src/Services/OrderService/OrderService.API \
  -o Persistence/Migrations

# NotificationService
dotnet ef migrations add Init \
  --project src/Services/NotificationService/NotificationService.Infrastructure \
  --startup-project src/Services/NotificationService/NotificationService.API \
  -o Persistence/Migrations
```

Sau khi chạy sẽ có 3 file mới trong `Persistence/Migrations/` của mỗi service:

```
<timestamp>_<TenMigration>.cs            ← Up()/Down() — DDL chính
<timestamp>_<TenMigration>.Designer.cs   ← snapshot tại thời điểm migration
<DbContextName>ModelSnapshot.cs          ← snapshot tổng (luôn cập nhật)
```

Commit cả 3 file vào git.

### Quy ước đặt tên

- `Init` — migration đầu tiên (tạo toàn bộ schema từ model hiện tại).
- Sau đó: dùng động từ + danh từ, ví dụ `AddPhoneToUser`,
  `RenameOrderStatus`, `AddIndexOnOrderCustomerId`. Hạn chế tên kiểu
  `Update1`, `Fix2` — không tra ra ý nghĩa khi cần rollback.

## 3. Apply migration vào DB

### 3.1 Tự động khi service khởi động (mặc định)

Mỗi service có đoạn này trong `Program.cs`:

```csharp
static async Task EnsureDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var attempts = 0;
    while (attempts < 10)
    {
        try { await db.Database.MigrateAsync(); return; }
        catch (Exception ex) { attempts++; await Task.Delay(TimeSpan.FromSeconds(3)); }
    }
}
```

→ Chạy `docker compose up --build` (hoặc `dotnet run`) là tự xong.

### 3.2 Thủ công (khi cần debug / chạy tay)

```bash
dotnet ef database update \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API
```

Apply tới một migration cụ thể:

```bash
dotnet ef database update <TenMigration> \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API
```

Connection string lấy từ `appsettings.{Environment}.json` của startup project.
Mặc định khi chạy tay (không qua docker) là `appsettings.Development.json`.

## 4. Rollback / xóa migration

### 4.1 Migration **chưa** được apply lên bất kỳ DB nào

```bash
dotnet ef migrations remove \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API
```

Lệnh này xóa migration mới nhất + cập nhật snapshot.

### 4.2 Migration **đã** được apply

Phải hạ DB về migration trước rồi mới remove:

```bash
# Quay DB về migration <PreviousName>
dotnet ef database update <PreviousName> \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API

# Sau đó remove file migration cuối
dotnet ef migrations remove \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API
```

> **KHÔNG** sửa file migration đã merge vào main. Tạo migration mới để fix.

## 5. Reset toàn bộ DB (dev only)

Khi đang dev và muốn xóa sạch:

```bash
docker compose down -v        # -v xóa cả volume hdos-sqldata
docker compose up --build
```

Migration sẽ chạy lại từ đầu, các bảng mới hoàn toàn rỗng.

## 6. Sinh SQL script (production-friendly)

Không phải lúc nào cũng muốn để service tự `Migrate` lúc startup (race condition
khi có nhiều replica, không có quyền DDL ở runtime, v.v.). Sinh script idempotent
để DBA chạy:

```bash
dotnet ef migrations script \
  --idempotent \
  --output ./out/auth-migrate.sql \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.API
```

Cờ `--idempotent` cho phép chạy nhiều lần — script tự kiểm tra
`__EFMigrationsHistory` trước khi apply mỗi migration.

## 7. Troubleshooting

| Triệu chứng | Nguyên nhân | Cách fix |
|---|---|---|
| `Invalid object name 'Users'` (Sql 208) khi gọi API | Chưa có migration nào trong source, hoặc migration chưa được apply | Tạo migration `Init` (mục 2) rồi `docker compose up --build` |
| `dotnet-ef does not exist` | Tool chưa cài / chưa có trong PATH | `dotnet tool install --global dotnet-ef --version "8.0.*"` + thêm `~/.dotnet/tools` vào PATH |
| `You must install .NET to run this application` khi chạy `dotnet ef` | Thiếu .NET 8 runtime trên máy host | Cài runtime 8 hoặc `export DOTNET_ROOT="$HOME/.dotnet"` nếu đã có ở folder user |
| `Your startup project doesn't reference Microsoft.EntityFrameworkCore.Design` | API csproj thiếu package design-time | `dotnet add src/Services/<Service>/<Service>.API package Microsoft.EntityFrameworkCore.Design --version 8.0.10` |
| `Unable to create a 'DbContext' of type ...` | EF không build được startup project, hoặc connection string sai | Đọc kỹ stack trace; thử `dotnet build` riêng startup project trước |
| Migration đã commit nhưng container vẫn lỗi `Invalid object name` | Image cũ không có file migration | `docker compose build --no-cache <service>` rồi `up -d` |

## 8. Khi thêm `DbContext` mới (service mới)

1. Tạo `*.Infrastructure` + `*.API` theo template (xem [10-them-feature-moi.md](./10-them-feature-moi.md)).
2. Thêm `Microsoft.EntityFrameworkCore.Design` vào API csproj.
3. Trong `DependencyInjection.cs`, set `MigrationsAssembly(typeof(<DbContext>).Assembly.FullName)`
   để migration nằm cùng `DbContext`.
4. Thêm `EnsureDatabaseAsync(app)` vào `Program.cs` (copy từ service đã có).
5. Tạo migration `Init` (mục 2).
6. Commit.
