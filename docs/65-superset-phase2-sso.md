# 65. Apache Superset — Phase 2: SSO với AuthService

> Tự động login Superset bằng JWT đã có khi user login Hdos — không cần admin/admin nữa.
> Tiền đề: đã làm [Phase 1](./64-superset-phase1-standalone.md).

## 1. TL;DR

- User login Hdos FE → AuthService cấp JWT (HS256)
- Click "Open Superset" → FE gọi `POST /auth/superset/sso` → AuthService set cookie `hdos_jwt` scope `/superset/` → trả `redirectUrl`
- FE `window.location.href = redirectUrl` → browser navigate `/superset/` mang cookie
- Superset Python custom Security Manager đọc cookie → validate JWT (cùng `Jwt__Secret`) → auto-create user + login

**Không OAuth2/OIDC.** Chỉ HMAC share secret. Đơn giản, đủ dùng cho SSO nội bộ. Tech debt RS256+JWKS ghi ở section 9.

## 2. Sequence diagram

```
┌────────┐       ┌──────────┐         ┌──────────────┐         ┌──────────┐
│   FE   │       │  nginx   │         │ AuthService  │         │ Superset │
└───┬────┘       └────┬─────┘         └──────┬───────┘         └────┬─────┘
    │                 │                       │                       │
    │  user click "Open Superset"             │                       │
    │ POST /auth/superset/sso                 │                       │
    │ Authorization: Bearer <jwt>             │                       │
    │ credentials: include                    │                       │
    ├────────────────►│──────────────────────►│                       │
    │                 │                       │                       │
    │                 │                       │ [Authorize]: validate JWT
    │                 │                       │ Response.Cookies.Append(
    │                 │                       │   "hdos_jwt", token,  │
    │                 │                       │   Path="/superset/",  │
    │                 │                       │   HttpOnly, Secure)   │
    │                 │                       │                       │
    │ 200 { redirectUrl }                     │                       │
    │ Set-Cookie: hdos_jwt=...; Path=/superset│                       │
    │◄────────────────│◄──────────────────────│                       │
    │                 │                       │                       │
    │  window.location.href = redirectUrl     │                       │
    │ GET /superset/   │                       │                       │
    │ Cookie: hdos_jwt=...                    │                       │
    ├────────────────►│───────────────────────────────────────────────►│
    │                 │                       │                       │
    │                 │                       │                       │ before_request hook:
    │                 │                       │                       │   - extract cookie
    │                 │                       │                       │   - decode JWT (HS256)
    │                 │                       │                       │   - find_or_create_user
    │                 │                       │                       │   - login_user()
    │                 │                       │                       │
    │ 302 /superset/welcome/                  │                       │
    │◄─────────────────────────────────────────────────────────────────┤
    │  Superset session cookie set                                    │
    │  (browser dùng session cookie cho mọi request sau)              │
```

## 3. Components

### 3.1. `superset/security_manager.py` (Python)

Class `HdosSecurityManager` kế thừa `SupersetSecurityManager`. Register `before_request` hook trong `__init__` (Flask app đã exist tại thời điểm SM được instantiate).

Hook logic:
1. Skip static assets + nếu user đã login
2. Extract JWT từ:
   - Header `Authorization: Bearer <jwt>` (ưu tiên — cho API call)
   - Cookie `hdos_jwt` (browser navigate)
3. Validate JWT bằng `HDOS_JWT_SECRET` (cùng giá trị với `Jwt__Secret` của AuthService), check Issuer/Audience/Expiry
4. Map claims → role:
   - `permission` chứa `superset:admin` → Superset Admin role
   - `permission` chứa `superset:editor` → Superset Alpha role
   - mặc định → Superset Gamma role (read-only)
5. `find_user(username)` hoặc auto-create bằng `add_user(...)`
6. `login_user(user)` — Flask-Login set session cookie

### 3.2. `superset/superset_config.py` (Python config)

```python
from security_manager import HdosSecurityManager
CUSTOM_SECURITY_MANAGER = HdosSecurityManager
HDOS_JWT_SECRET = os.environ.get("HDOS_JWT_SECRET", "")
```

`CUSTOM_SECURITY_MANAGER` là entry point của Flask-AppBuilder. Khi Superset boot, nó instantiate class này thay cho default.

### 3.3. `src/Services/AuthService/AuthService.API/Controllers/SupersetController.cs`

Endpoint:

| Method | Path | Auth | Mục đích |
|--------|------|------|----------|
| POST | `/auth/superset/sso` | `[Authorize]` | Set cookie `hdos_jwt` + trả redirectUrl |
| POST | `/auth/superset/guest-token` | `[Authorize]` | Phase 4 — issue embedded token |
| POST | `/auth/superset/logout` | `[Authorize]` | Delete cookie `hdos_jwt` |

SSO endpoint code chính:
```csharp
[Authorize]
[HttpPost("sso")]
public IActionResult Sso()
{
    var token = ExtractBearerToken();   // từ Authorization: Bearer header
    Response.Cookies.Append("hdos_jwt", token, new CookieOptions
    {
        Path = "/superset/",            // chỉ gửi cho path /superset/*
        HttpOnly = true,                // JS không đọc được — chống XSS
        Secure = true,                  // chỉ HTTPS
        SameSite = SameSiteMode.Lax,    // không gửi cross-site form POST
        Expires = ...                   // match JWT lifetime
    });
    return Ok(new { redirectUrl = "https://localhost:8444/" });
}
```

### 3.4. Compose env wiring

| Service | Env var | Value source |
|---------|---------|--------------|
| `superset` | `HDOS_JWT_SECRET` | `${JWT_SECRET}` (cùng với AuthService) |
| `authservice` | `Superset__PublicUrl` | `${SUPERSET_PUBLIC_URL}` |

JWT secret PHẢI giống nhau giữa AuthService (`Jwt__Secret`) và Superset (`HDOS_JWT_SECRET`).

## 4. Frontend integration

```typescript
// FE: gọi khi user click "Open Superset"
async function openSuperset() {
  const jwt = localStorage.getItem('hdos_jwt');
  const res = await fetch('/auth/superset/sso', {
    method: 'POST',
    headers: { Authorization: `Bearer ${jwt}` },
    credentials: 'include',  // QUAN TRỌNG: nhận Set-Cookie
  });
  if (!res.ok) {
    alert('Không vào được Superset, login lại Hdos');
    return;
  }
  const { data } = await res.json();
  window.location.href = data.redirectUrl;
}
```

**Lưu ý FE:**
- `credentials: 'include'` bắt buộc — nếu thiếu, browser bỏ qua Set-Cookie header
- Domain FE và AuthService phải cùng eTLD+1 (Hdos đã đi qua nginx gateway nên OK)
- Sau khi navigate xong, Superset có session cookie riêng — Hdos cookie `hdos_jwt` chỉ dùng để bootstrap login lần đầu

## 5. Role/Permission mapping chi tiết

| JWT `permission` claim | Superset Role | Quyền |
|-----------------------|---------------|-------|
| `superset:admin` | `Admin` | Full — manage users, datasources, dashboards |
| `superset:editor` | `Alpha` | Create dashboards/charts (không manage users) |
| (default) | `Gamma` | Read-only — chỉ xem dashboard được share |

**Cách cấp permission `superset:admin` cho user Hdos:**

1. Vào AuthService UI/API: tạo Permission `superset:admin` (nếu chưa có)
2. Tạo Role `BiAdmin` chứa permission `superset:admin`
3. Assign Role `BiAdmin` cho user
4. User logout + login lại → JWT mới chứa `permission: ["superset:admin", ...]`
5. Click "Open Superset" → Security Manager Python đọc claim → user được map thành Superset Admin

Nếu user đã từng login Superset trước đó với role thấp hơn, Security Manager sẽ **update role** (logic ở `_find_or_create_user` line "Update role if changed").

## 6. Cấu hình & deploy

### 6.1. Local dev

Sau khi sync code mới:

```bash
# Rebuild Superset (vì Dockerfile copy security_manager.py mới)
docker compose build superset
docker compose up -d --force-recreate superset

# Rebuild AuthService (vì có SupersetController mới)
docker compose build authservice
docker compose up -d --force-recreate authservice

# Test SSO end-to-end
curl -k -X POST https://localhost:8443/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@hdos.dev","password":"Admin1234!"}'
# Lấy token từ response.data.token

JWT='<paste token>'
curl -k -X POST https://localhost:8443/auth/superset/sso \
  -H "Authorization: Bearer $JWT" \
  -c cookies.txt
# Kiểm tra cookies.txt phải có hdos_jwt với Path=/superset/

# Mở browser DevTools, paste cookie vào, navigate https://localhost:8444/
# → vào thẳng dashboard, không cần admin/admin
```

### 6.2. Staging/Prod

Sau khi merge PR vào `main`:
- CD pipeline tự rebuild Superset + AuthService images
- `JWT_SECRET` đã set sẵn ở `/opt/hdos-prod/.env` → đẩy thẳng vào cả `authservice` và `superset` container

Không cần config gì thêm.

## 7. Bảo mật — đánh giá

### 7.1. Cookie `hdos_jwt`

- ✅ `HttpOnly` → JavaScript XSS không đọc được token
- ✅ `Secure` → chỉ gửi qua HTTPS
- ✅ `SameSite=Lax` → CSRF safe cho POST cross-site
- ✅ `Path=/superset/` → KHÔNG leak sang `/auth/`, `/lakehouse/`, ...
- ✅ Cookie expire match JWT TTL (480 phút mặc định)

### 7.2. Trade-off: HS256 share secret

| Risk | Impact |
|------|--------|
| Container Superset bị compromise → attacker có `JWT_SECRET` → forge JWT cho mọi service | Cao — toàn hệ thống |
| Container AuthService bị compromise → tương tự | Cao (vốn dĩ AuthService đã giữ secret này) |

**Giảm rủi ro:**
- Network isolation: Superset không expose port host (chỉ nginx proxy)
- `superset_config.py` không log JWT secret
- Audit log của Superset (login attempts) → giám sát anomaly

### 7.3. Tech debt: migrate RS256 + JWKS

Pattern chuẩn hơn:
1. AuthService dùng RS256 (asymmetric) thay HS256
2. AuthService expose JWKS endpoint `/.well-known/jwks.json` chứa **public key**
3. Mọi service consumer (Superset, OrderService, ...) fetch public key từ JWKS để validate
4. **Private key chỉ AuthService có** → Superset bị compromise cũng không forge được JWT

Effort ước tính: 3-5 ngày (đổi AuthService key handling + update mọi service validation + key rotation strategy). Để Phase 7 nếu có nhu cầu compliance/audit yêu cầu.

## 8. Troubleshooting

### 8.1. Vào `/superset/` vẫn thấy form login admin/admin

Hook không chạy hoặc JWT invalid. Check theo thứ tự:

```bash
# 1. Cookie có được gửi không?
# Browser DevTools → Application → Cookies → localhost:8443
# Phải thấy hdos_jwt, Path=/superset/, HttpOnly ✓

# 2. Superset có nhận được JWT_SECRET không?
docker compose exec superset printenv | grep HDOS_JWT_SECRET
# Phải khớp với JWT_SECRET của authservice
docker compose exec authservice printenv | grep Jwt__Secret

# 3. Logs có gì?
docker compose logs superset | grep -i "HdosSecurityManager\|SSO\|JWT"
# Mong đợi:
#   HdosSecurityManager: SSO before_request hook registered (lúc boot)
#   Hdos SSO login OK: user=... roles=[...]  (lúc login)
#   Invalid Hdos JWT: ...                    (nếu JWT sai)
```

### 8.2. `Superset role 'Admin' không tồn tại`

Lần đầu boot, `superset init` chưa chạy. Re-run:
```bash
docker compose run --rm superset-init
```

### 8.3. User Hdos login Superset nhưng role không đúng

- Check JWT có claim `permission` không: `echo $JWT | cut -d. -f2 | base64 -d | jq`
- Nếu claim đúng nhưng role Superset sai → có thể cache. Logout Superset + xóa cookie session + login lại.

### 8.4. `Invalid issuer`

`JWT_SECRET` đúng nhưng Issuer/Audience config không match. Đảm bảo:
- AuthService set `Jwt__Issuer=hdos-auth`, `Jwt__Audience=hdos-api`
- Security Manager Python check cùng giá trị (hardcode trong `security_manager.py`)

## 9. Done criteria — Phase 2

- [x] `superset/security_manager.py` chạy không lỗi syntax
- [x] `CUSTOM_SECURITY_MANAGER = HdosSecurityManager` trong `superset_config.py`
- [x] `HDOS_JWT_SECRET` truyền vào Superset container = `Jwt__Secret` của AuthService
- [x] `POST /auth/superset/sso` trả `{ redirectUrl }` + Set-Cookie `hdos_jwt`
- [x] Build AuthService pass: `dotnet build src/Services/AuthService/AuthService.API` → 0 errors
- [ ] **Test manual:** Login Hdos → click "Open Superset" → vào thẳng dashboard không thấy form admin/admin

## 10. Liên kết

- [Phase 1 — Standalone setup](./64-superset-phase1-standalone.md)
- [Phase 4 — Embedded SDK for FE](./66-superset-phase4-fe-embedded-guide.md)
- [Doc 06 — Hdos JWT & RBAC](./06-xac-thuc.md)
