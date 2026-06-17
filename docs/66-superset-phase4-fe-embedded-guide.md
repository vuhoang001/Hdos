# 66. Apache Superset — Phase 4: Embedded Dashboard (FE Guide)

> Nhúng dashboard Superset vào FE Hdos qua iframe + guest token. Cho end-user (bác sĩ, quản lý bệnh viện) xem dashboard mà không cần login Superset.
> Tiền đề: đã làm [Phase 1](./64-superset-phase1-standalone.md) + [Phase 2](./65-superset-phase2-sso.md).

## 1. TL;DR

- **Admin (BI team):** vào Superset, build dashboard, bật **Embed dashboard** → nhận `dashboardUuid`
- **FE Hdos:** dùng `@superset-ui/embedded-sdk`
  ```ts
  embedDashboard({
    id: dashboardUuid,
    supersetDomain: 'https://localhost:8443',
    mountPoint: document.getElementById('dashboard'),
    fetchGuestToken: async () => {
      const res = await fetch('/auth/superset/guest-token', {
        method: 'POST',
        headers: { Authorization: `Bearer ${jwt}` },
        body: JSON.stringify({ dashboardId: dashboardUuid, ... })
      });
      const { data } = await res.json();
      return data.token;
    }
  });
  ```
- **BE:** `POST /auth/superset/guest-token` — proxy gọi Superset Admin API issue token

## 2. Vì sao guest token, không phải SSO?

| Use case | Authentication |
|----------|----------------|
| Admin/BI user vào Superset trực tiếp build dashboard | **SSO (Phase 2)** — full session, full UI |
| End-user xem dashboard nhúng trong FE Hdos | **Guest token (Phase 4)** — chỉ xem, không truy cập UI Superset |

Guest token:
- TTL ngắn (~5 phút — set trong `superset_config.py`: `GUEST_TOKEN_JWT_EXP_SECONDS`)
- Scope hẹp: chỉ xem các dashboard được liệt kê trong `resources`
- Hỗ trợ Row-Level Security (RLS) — filter data theo user (VD: bác sĩ chỉ thấy bệnh nhân của mình)
- KHÔNG cho phép access SQL Lab / settings / user management

## 3. Sequence diagram

```
┌────┐         ┌──────────┐         ┌──────────────┐         ┌──────────┐
│ FE │         │  nginx   │         │ AuthService  │         │ Superset │
└─┬──┘         └────┬─────┘         └──────┬───────┘         └────┬─────┘
  │                 │                       │                       │
  │ POST /auth/superset/guest-token        │                       │
  │ Authorization: Bearer <user-jwt>       │                       │
  │ body: { dashboardId, username, ... }   │                       │
  ├────────────────►│──────────────────────►│                       │
  │                 │                       │                       │
  │                 │                       │ [Authorize] check user│
  │                 │                       │ MediatR.Send(cmd)     │
  │                 │                       │   ↓                   │
  │                 │                       │ SupersetAdminClient   │
  │                 │                       │   ↓ POST /api/v1/security/login
  │                 │                       │   ├──────────────────►│
  │                 │                       │   │◄────── admin_token─┤ (cache 30 phút)
  │                 │                       │   ↓ POST /api/v1/security/guest_token/
  │                 │                       │   │ Bearer admin_token│
  │                 │                       │   │ body: { user, resources, rls }
  │                 │                       │   ├──────────────────►│
  │                 │                       │   │◄────── guest_token─┤
  │                 │                       │                       │
  │ 200 { data: { token } }                │                       │
  │◄────────────────│◄──────────────────────│                       │
  │                                                                  │
  │  embedDashboard({ ... fetchGuestToken: () => token })           │
  │                                                                  │
  │  iframe src="https://.../superset/embedded/<dashboardId>"       │
  │  Authorization: Bearer <guest_token>                            │
  ├────────────────────────────────────────────────────────────────►│
  │                                                                  │
  │  rendered dashboard                                             │
  │◄────────────────────────────────────────────────────────────────┤
```

## 4. Admin steps — chuẩn bị dashboard cho embed

Trên Superset UI:

1. Build dashboard như bình thường (charts + filters + layout)
2. Mở dashboard → click menu **⋮** (3 chấm) → **Embed dashboard**
3. Trong dialog:
   - **Allowed Domains:** liệt kê domain FE (VD: `https://localhost:8443`, `https://hdos.example.com`)
   - Click **Enable embedding**
4. Copy **Embedded ID (UUID)** — dùng cho FE
5. Pop-up sẽ show code snippet — FE Hdos KHÔNG dùng snippet này vì đã có wrapper riêng (xem section 5)

> Embed UUID khác với Dashboard ID (slug). Dùng đúng UUID.

## 5. BE endpoint: `POST /auth/superset/guest-token`

**Path:** `POST https://<host>/auth/superset/guest-token`
**Auth:** `[Authorize]` — bắt buộc JWT Hdos hợp lệ
**Request body:**
```json
{
  "dashboardId": "abc12345-67ef-89gh-ijkl-mnopqrstuvwx",
  "username": "doctor_001",
  "firstName": "Nguyễn",
  "lastName": "Văn A"
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGc...long-jwt..."
  }
}
```

**Lỗi:**
| HTTP | ErrorCode | Nguyên nhân |
|------|-----------|-------------|
| 401 | — | JWT Hdos thiếu/sai (middleware) |
| 400 | `validation.error` | Body sai format (FluentValidation) |
| 400 | `validation.error` | Superset không reach được, hoặc dashboardId không enabled embed |

## 6. FE integration

### 6.1. Install SDK

```bash
npm install @superset-ui/embedded-sdk
```

### 6.2. Component example (React)

```tsx
import { useEffect, useRef } from 'react';
import { embedDashboard } from '@superset-ui/embedded-sdk';

interface Props {
  dashboardUuid: string;   // từ admin Superset
  height?: string;          // CSS height của iframe
}

export function SupersetDashboard({ dashboardUuid, height = '600px' }: Props) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!ref.current) return;
    const jwt = localStorage.getItem('hdos_jwt');
    if (!jwt) return;

    // Lấy thông tin user từ JWT để truyền cho audit log Superset
    const user = JSON.parse(atob(jwt.split('.')[1]));

    embedDashboard({
      id: dashboardUuid,
      supersetDomain: window.location.origin,   // https://localhost:8443
      mountPoint: ref.current,
      fetchGuestToken: async () => {
        const res = await fetch('/auth/superset/guest-token', {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${jwt}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            dashboardId: dashboardUuid,
            username: user.preferred_username || user.email,
            firstName: (user.name || user.email).split(' ')[0] || 'User',
            lastName: (user.name || '').split(' ').slice(1).join(' ') || '(Hdos)',
          }),
        });
        if (!res.ok) throw new Error(`Guest token failed: ${res.status}`);
        const { data } = await res.json();
        return data.token;
      },
      dashboardUiConfig: {
        hideTitle: true,
        hideTab: false,
        hideChartControls: false,
        filters: { expanded: true },
      },
    });
  }, [dashboardUuid]);

  return <div ref={ref} style={{ width: '100%', height }} />;
}
```

### 6.3. Sử dụng

```tsx
function PatientStatsPage() {
  // dashboardUuid lấy từ config — BE return hoặc hard-code theo từng page
  return (
    <div>
      <h1>Thống kê bệnh nhân</h1>
      <SupersetDashboard dashboardUuid="abc12345-67ef-..." height="800px" />
    </div>
  );
}
```

### 6.4. Token refresh

Guest token TTL = 5 phút. `@superset-ui/embedded-sdk` tự gọi lại `fetchGuestToken` callback trước khi token expire — **FE không cần làm gì thêm**. Đảm bảo `fetchGuestToken` luôn trả token mới mỗi lần được gọi.

## 7. Row-Level Security (RLS) — filter theo user

Khi nhiều user xem cùng dashboard nhưng phải thấy data khác nhau (VD: mỗi bác sĩ chỉ thấy bệnh nhân của mình).

### 7.1. Trên Superset (admin)

1. **Settings → Row Level Security**
2. Add filter: `clause = "doctor_id = '{{ current_username() }}'"`
3. Apply cho dataset cụ thể

### 7.2. Trên BE — extend `CreateGuestTokenCommand`

Mặc định BE gửi `rls: []`. Để pass RLS rule động, cần extend command + handler:

```csharp
public sealed record CreateGuestTokenCommand(
    Guid DashboardId,
    string Username,
    string FirstName,
    string LastName,
    IReadOnlyCollection<RlsRule>? Rls = null)   // ← thêm
    : IRequest<Result<GuestTokenDto>>;

public sealed record RlsRule(string Clause, IReadOnlyCollection<string>? Datasets = null);
```

Và `SupersetAdminClient.IssueGuestTokenAsync` build body với RLS array đó. Xem source `SupersetAdminClient.cs` cho format.

### 7.3. Trên FE

```tsx
body: JSON.stringify({
  dashboardId,
  username,
  firstName,
  lastName,
  rls: [{ clause: `doctor_id = '${currentUserId}'` }],   // ← thêm
}),
```

> Note Phase 4 hiện tại CHƯA implement RLS — bỏ trống `rls: []`. Khi có nhu cầu, extend theo hướng trên.

## 8. Config & env vars

### 8.1. Trên AuthService

| Env | Mục đích | Default (dev) |
|-----|----------|---------------|
| `Superset__BaseUrl` | URL nội bộ Superset (gọi Admin API) | `http://superset:8088/` |
| `Superset__AdminUsername` | Username admin Superset | `admin` |
| `Superset__AdminPassword` | Password admin Superset | `admin` (PROD: bắt buộc đổi) |
| `Superset__PublicUrl` | URL public Superset (FE redirect) | `https://localhost:8444/` |

### 8.2. Trên Superset container

| Env | Mục đích |
|-----|----------|
| `HDOS_FE_ORIGINS` | CORS allowlist (comma-separated). VD: `https://hdos.example.com,http://localhost:4000` |

Trong `superset_config.py`:
```python
ENABLE_CORS = True
CORS_OPTIONS = {
    "supports_credentials": True,
    "origins": [...]   # từ HDOS_FE_ORIGINS
}
HTTP_HEADERS = {"X-Frame-Options": "ALLOWALL"}   # cho iframe
TALISMAN_ENABLED = False                          # Superset Talisman tắt vì gây block iframe
```

## 9. Bảo mật

### 9.1. Guest token

- ✅ TTL 5 phút (refresh tự động)
- ✅ Scope hẹp — chỉ xem `resources` được liệt kê
- ✅ Không cho access SQL Lab / settings
- ⚠️ Khi FE compromise (XSS), attacker có thể call `/auth/superset/guest-token` với JWT user → nhận guest token → render dashboard
  - **Mitigation:** JWT đã `HttpOnly` cookie nếu FE đổi sang cookie auth; với localStorage hiện tại, XSS có thể đọc JWT — đây là tradeoff Hdos đã chấp nhận

### 9.2. Admin credentials Superset

- AuthService có `Superset__AdminPassword` — secret này cấp quyền admin Superset
- PROD: store trong vault hoặc env file `/opt/hdos-prod/.env` (chmod 600)
- Nếu password rotate, **invalidate cache** trong AuthService (restart container hoặc gọi internal endpoint clear cache — TODO Phase 7)

### 9.3. CORS

`HDOS_FE_ORIGINS` chỉ allow domain Hdos. Đừng đặt `*` (wildcard) ở production.

## 10. Cấu hình & deploy

### 10.1. Local dev

```bash
# 1. Pull code mới
git pull

# 2. Rebuild AuthService (có endpoint mới + SupersetAdminClient)
docker compose build authservice
docker compose up -d --force-recreate authservice

# 3. Rebuild Superset (config có thêm EMBEDDED_SUPERSET + CORS)
docker compose build superset
docker compose up -d --force-recreate superset

# 4. Bật embed cho dashboard trên Superset UI (xem section 4)

# 5. Test guest token endpoint
JWT=$(curl -k -s -X POST https://localhost:8443/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@hdos.dev","password":"Admin1234!"}' \
  | jq -r '.data.token')

curl -k -X POST https://localhost:8443/auth/superset/guest-token \
  -H "Authorization: Bearer $JWT" \
  -H "Content-Type: application/json" \
  -d '{
    "dashboardId": "<paste-embed-uuid>",
    "username": "test_user",
    "firstName": "Test",
    "lastName": "User"
  }'
# Expected: { "success": true, "data": { "token": "..." } }
```

### 10.2. Staging/Prod

- CD pipeline tự rebuild + redeploy
- `SUPERSET_ADMIN_PASSWORD` đã set ở `/opt/hdos-prod/.env`
- Nếu FE deploy ở domain khác AuthService, set `HDOS_FE_ORIGINS` trong `.env`

## 11. Troubleshooting

### 11.1. Iframe trống trơn

```
Refused to display 'https://.../superset/embedded/...' in a frame
because it set 'X-Frame-Options' to 'sameorigin'
```

Superset Talisman vẫn enabled. Kiểm tra:
```bash
docker compose exec superset python -c "from superset.app import create_app; app = create_app(); print(app.config.get('TALISMAN_ENABLED'))"
# Phải False
```

### 11.2. Iframe load nhưng báo "Invalid guest token"

- JWT_SECRET không match — Superset không validate được token nó tự issue
- Hoặc admin Superset password sai → AuthService không login được
- Check logs:
```bash
docker compose logs authservice | grep -i superset
docker compose logs superset | grep -i "guest_token\|login"
```

### 11.3. CORS error trên browser

```
Access to fetch at 'https://.../superset/embedded/...' from origin 'https://hdos-frontend' has been blocked by CORS
```

`HDOS_FE_ORIGINS` không include FE domain. Update env + restart Superset.

### 11.4. Dashboard render nhưng filter không hoạt động

Native filters yêu cầu `DASHBOARD_NATIVE_FILTERS = True` trong feature flags — đã bật mặc định, nhưng check:
```bash
docker compose exec superset python -c "from superset.app import create_app; app = create_app(); print(app.config['FEATURE_FLAGS'])"
```

### 11.5. Token expired sau 5 phút, dashboard freeze

`@superset-ui/embedded-sdk` phải tự refresh. Nếu không:
- Check `fetchGuestToken` callback có return Promise đúng không
- Check console error có `401 Unauthorized` khi gọi `/auth/superset/guest-token` không (JWT user expired → refresh JWT)

## 12. Done criteria — Phase 4

- [x] `EMBEDDED_SUPERSET = True` trong `superset_config.py`
- [x] CORS + TALISMAN_ENABLED=False để cho phép iframe
- [x] `POST /auth/superset/guest-token` endpoint hoạt động
- [x] `SupersetAdminClient` cache admin token 30 phút
- [x] Build AuthService pass: `dotnet build` → 0 errors
- [ ] **Test manual FE:** nhúng dashboard mẫu trong page Hdos, render được chart

## 13. Liên kết

- [Phase 1 — Standalone setup](./64-superset-phase1-standalone.md)
- [Phase 2 — SSO với AuthService](./65-superset-phase2-sso.md)
- [Superset Embedded SDK docs](https://www.npmjs.com/package/@superset-ui/embedded-sdk) (external)
- [Superset Guest Token API](https://superset.apache.org/docs/api/#tag/Security/operation/SecurityRestApi.guest_token) (external)
