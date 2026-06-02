# 31 — License Management

## Mục lục

1. [Tổng quan](#1-tổng-quan)
2. [Kiến trúc](#2-kiến-trúc)
3. [Cách hoạt động](#3-cách-hoạt-động)
4. [Plans & Modules](#4-plans--modules)
5. [Cấu trúc JWT sau khi thêm license](#5-cấu-trúc-jwt-sau-khi-thêm-license)
6. [API Reference](#6-api-reference)
7. [Hướng dẫn sử dụng cho Admin](#7-hướng-dẫn-sử-dụng-cho-admin)
8. [Bảo vệ endpoint bằng license](#8-bảo-vệ-endpoint-bằng-license)
9. [Thêm module mới](#9-thêm-module-mới)
10. [Tạo EF Migration khi không có SQL Server local](#10-tạo-ef-migration-khi-không-có-sql-server-local)
11. [Seed data mặc định](#11-seed-data-mặc-định)

---

## 1. Tổng quan

License Management cho phép kiểm soát **user nào được dùng module nào** trong hệ thống HDOS. Thay vì dựng thêm service riêng, license được **nhúng trực tiếp vào JWT** hiện có — không tốn thêm round-trip API, mọi service validate offline bằng token đang có sẵn.

```
Admin gán license → User login → JWT chứa license claims → Service đọc claim → Allow/Block
```

---

## 2. Kiến trúc

```
AuthService
├── Domain
│   └── Entities/UserLicense.cs          ← entity chính
│   └── Repositories/IUserLicenseRepository.cs
│
├── Infrastructure
│   └── Persistence/UserLicenseRepository.cs
│   └── Persistence/Configurations/UserLicenseConfiguration.cs
│   └── Persistence/AuthDbContextFactory.cs   ← design-time factory (migrations local)
│   └── Migrations/..._AddUserLicenses.cs
│
├── Application
│   ├── Features/License/AssignLicenseCommand.cs
│   ├── Features/License/RevokeLicenseCommand.cs
│   └── Features/License/GetUserLicenseQuery.cs
│   └── DTOs/LicenseDto.cs
│
└── API/Controllers/LicensesAdminController.cs  ← REST endpoints

BuildingBlocks/Common/Auth
├── LicenseClaimTypes.cs   ← claim name constants (lic_plan, lic_mod, lic_exp)
├── HdosModules.cs         ← module slug constants + HdosLicensePolicies
├── IJwtTokenIssuer.cs     ← thêm LicenseInfo? parameter
├── JwtTokenIssuer.cs      ← embed license claims vào JWT
└── JwtAuthExtensions.cs   ← đăng ký license policies
```

**Database:** bảng `UserLicenses` trong `AuthDb` (SQL Server).

---

## 3. Cách hoạt động

### Luồng gán license

```
Admin  POST /auth/admin/licenses
         │
         ▼
   AssignLicenseCommand
         │  revoke license cũ (nếu có)
         │  tạo UserLicense mới
         ▼
   AuthDb.UserLicenses
```

### Luồng login

```
User   POST /auth/login
         │
         ▼
   LoginUserCommandHandler
         │  1. verify password
         │  2. query roles + permissions  (như cũ)
         │  3. query UserLicenses WHERE UserId = ? AND IsActive = true
         │  4. nếu có license và chưa hết hạn → tạo LicenseInfo
         ▼
   JwtTokenIssuer.Issue(..., licenseInfo)
         │  embed lic_plan, lic_mod[], lic_exp vào JWT claims
         ▼
   JWT trả về client
```

### Luồng validate tại service

```
Client  GET /m01/something
  Authorization: Bearer <jwt>
         │
         ▼
   M01Service middleware
         │  verify JWT signature (offline, không cần gọi AuthService)
         │  đọc claim "lic_mod" → ["m01", "orders", ...]
         ▼
   [Authorize(Policy = HdosLicensePolicies.ModuleM01)]
         │  claim "lic_mod" chứa "m01" → 200 OK
         │  không có → 403 Forbidden
```

---

## 4. Plans & Modules

### Plans (gợi ý, không enforce cứng)

| Plan | Ý nghĩa gợi ý |
|------|---------------|
| `free` | Dùng thử, giới hạn module |
| `basic` | Gói cơ bản |
| `pro` | Gói nâng cao |
| `enterprise` | Toàn quyền |

Plan chỉ là metadata — logic "plan X được dùng module nào" do admin quyết định khi gán license.

### Modules (constants trong `HdosModules`)

| Constant | Slug | Service tương ứng |
|----------|------|-------------------|
| `HdosModules.Orders` | `orders` | OrderService |
| `HdosModules.Notifications` | `notifications` | NotificationService |
| `HdosModules.M01` | `m01` | M01Service |
| `HdosModules.DataMatching` | `data-matching` | DataMatchingService |
| `HdosModules.Forms` | `forms` | DynamicFormService |
| `HdosModules.Async` | `async` | AsyncGateway |

---

## 5. Cấu trúc JWT sau khi thêm license

```json
{
  "sub": "a1b2c3d4-...",
  "email": "doctor@hospital.vn",
  "name": "Bác sĩ A",
  "roles": ["user"],
  "permission": ["orders:read", "m01:read", "m01:write"],

  "lic_plan": "pro",
  "lic_mod":  "orders",
  "lic_mod":  "m01",
  "lic_mod":  "notifications",
  "lic_exp":  "2027-01-01T00:00:00.0000000Z",

  "iat": 1748822400,
  "exp": 1748851200
}
```

> `lic_mod` là multi-value claim — JWT chứa nhiều claim cùng tên, mỗi cái là 1 module.
>
> `lic_exp` là expiry của **license** (khác với `exp` là expiry của **token**).
> User có thể login sau ngày hết license nhưng `lic_mod` sẽ không được embed → service block.

---

## 6. API Reference

Base URL: `/auth/admin/licenses` — yêu cầu role `admin`.

---

### GET `/auth/admin/licenses/{userId}`

Lấy license đang active của user.

**Response 200**
```json
{
  "success": true,
  "data": {
    "id": "f1e2d3c4-...",
    "userId": "a1b2c3d4-...",
    "plan": "pro",
    "modules": ["orders", "m01", "notifications"],
    "expiresAtUtc": "2027-01-01T00:00:00Z",
    "isActive": true,
    "isExpired": false,
    "createdAtUtc": "2026-06-02T01:36:00Z"
  }
}
```

**Response 404** — user không có license active.

---

### POST `/auth/admin/licenses`

Gán license cho user. Nếu đã có license active → tự động revoke và tạo mới.

**Request body**
```json
{
  "userId": "a1b2c3d4-...",
  "plan": "pro",
  "modules": ["orders", "m01", "notifications", "forms"],
  "expiresAtUtc": "2027-01-01T00:00:00Z"
}
```

> `expiresAtUtc: null` → license vĩnh viễn, không encode `lic_exp` vào JWT.

**Response 200** — trả về `LicenseDto` của license vừa tạo.

**Response 400** — validation error (userId rỗng, plan rỗng...).

---

### DELETE `/auth/admin/licenses/{userId}`

Revoke license active của user. User vẫn tồn tại, nhưng lần login tiếp theo JWT sẽ không có `lic_mod` claims.

**Response 200**
```json
{ "success": true }
```

**Response 404** — user không có license active.

---

## 7. Hướng dẫn sử dụng cho Admin

### Gán license mới cho user

```bash
# Lấy token admin trước
TOKEN=$(curl -s -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@hdos.dev","password":"Admin1234!"}' \
  | jq -r '.data.token')

# Gán license pro cho user, có hiệu lực 1 năm
curl -X POST http://localhost:5000/auth/admin/licenses \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "USER_ID_ở_đây",
    "plan": "pro",
    "modules": ["orders", "m01", "notifications", "forms"],
    "expiresAtUtc": "2027-06-01T00:00:00Z"
  }'
```

### Xem license hiện tại của user

```bash
curl http://localhost:5000/auth/admin/licenses/USER_ID_ở_đây \
  -H "Authorization: Bearer $TOKEN"
```

### Thu hồi license

```bash
curl -X DELETE http://localhost:5000/auth/admin/licenses/USER_ID_ở_đây \
  -H "Authorization: Bearer $TOKEN"
```

### Nâng cấp plan (thay license)

Chỉ cần POST lại — hệ thống tự revoke cũ và tạo mới:

```bash
curl -X POST http://localhost:5000/auth/admin/licenses \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "USER_ID_ở_đây",
    "plan": "enterprise",
    "modules": ["orders","m01","notifications","forms","data-matching","async"],
    "expiresAtUtc": null
  }'
```

> Sau khi thay đổi license, user cần **login lại** để nhận JWT mới có claims cập nhật.

---

## 8. Bảo vệ endpoint bằng license

### Bước 1 — Đảm bảo service gọi `AddHdosAuthorization()`

Tất cả service đều đã gọi `AddHdosAuthorization()` trong `Program.cs`, hàm này đã đăng ký sẵn tất cả license policies.

### Bước 2 — Thêm `[Authorize]` vào controller/action

```csharp
using Hdos.Common.Auth;

// Chỉ user có license module "m01" mới được vào
[Authorize(Policy = HdosLicensePolicies.ModuleM01)]
[HttpGet("patients")]
public async Task<IActionResult> GetPatients(...)

// Kết hợp permission + license
[Authorize(Policy = HdosPermissions.M01Read)]
[Authorize(Policy = HdosLicensePolicies.ModuleM01)]
[HttpGet("patients/{id}")]
public async Task<IActionResult> GetPatient(...)
```

### Bước 3 — Đọc thông tin license trong code (tuỳ chọn)

```csharp
// Đọc plan từ claim
var plan = User.FindFirstValue(LicenseClaimTypes.Plan);  // "pro"

// Đọc danh sách modules
var modules = User.FindAll(LicenseClaimTypes.Module)
                  .Select(c => c.Value)
                  .ToList();  // ["m01", "orders", ...]

// Check thủ công nếu không dùng policy
var hasM01 = User.HasClaim(LicenseClaimTypes.Module, HdosModules.M01);
```

### Bảng policy ↔ module

| Policy constant | Module slug | Dùng cho service |
|----------------|-------------|-----------------|
| `HdosLicensePolicies.ModuleOrders` | `orders` | OrderService |
| `HdosLicensePolicies.ModuleNotifications` | `notifications` | NotificationService |
| `HdosLicensePolicies.ModuleM01` | `m01` | M01Service |
| `HdosLicensePolicies.ModuleDataMatching` | `data-matching` | DataMatchingService |
| `HdosLicensePolicies.ModuleForms` | `forms` | DynamicFormService |
| `HdosLicensePolicies.ModuleAsync` | `async` | AsyncGateway |

---

## 9. Thêm module mới

Ví dụ thêm module `billing`:

**1. Thêm constant vào `HdosModules.cs`**

```csharp
public const string Billing = "billing";

public static readonly IReadOnlyList<string> All = [
    Orders, Notifications, M01, DataMatching, Forms, Async, Billing,  // ← thêm
];
```

**2. Thêm policy vào `HdosLicensePolicies` (cùng file)**

```csharp
public const string ModuleBilling = "license:billing";
```

**3. Đăng ký policy trong `JwtAuthExtensions.cs`**

```csharp
options.AddPolicy(HdosLicensePolicies.ModuleBilling,
    p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.Billing));
```

**4. Dùng trong BillingService controller**

```csharp
[Authorize(Policy = HdosLicensePolicies.ModuleBilling)]
```

Không cần migration, không cần thay đổi DB — module slug chỉ là string trong `ModulesCsv`.

---

## 10. Tạo EF Migration khi không có SQL Server local

Dự án dùng `IDesignTimeDbContextFactory` tại
`AuthService.Infrastructure/Persistence/AuthDbContextFactory.cs`.

Factory cung cấp connection string giả — EF chỉ đọc schema, **không cần SQL Server thật**.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

dotnet ef migrations add <TênMigration> \
  --project src/Services/AuthService/AuthService.Infrastructure \
  --startup-project src/Services/AuthService/AuthService.Infrastructure
```

Migration được apply tự động khi app khởi động trên server qua `MigrateAsync()` trong `Program.cs`.

---

## 11. Seed data mặc định

`AuthDataSeeder` tự động tạo license cho 2 user seed:

| User | Plan | Modules | Expiry |
|------|------|---------|--------|
| `admin@hdos.dev` | `enterprise` | Tất cả modules | Vĩnh viễn |
| `testuser@hdos.dev` | `basic` | `orders`, `notifications` | +1 năm từ ngày deploy |

Seed chỉ chạy nếu user chưa có license active — idempotent, an toàn khi restart.
