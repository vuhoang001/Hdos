---
title: How to debug lỗi 401
sidebar_position: 4
description: Bảng nguyên nhân và checklist khi request bị 401.
tags: [how-to, debugging, jwt]
---

# How-to — Debug lỗi 401 Unauthorized

## Checklist nhanh

1. **Có header không?** `curl -v` xem có `Authorization: Bearer ...` không.
2. **Token còn hạn không?** Decode tại [jwt.io](https://jwt.io), check `exp` so với `DateTime.UtcNow`.
3. **Đúng `iss` / `aud` không?** Phải khớp `Jwt:Issuer` / `Jwt:Audience` trong `appsettings.json`.
4. **Đúng `Secret` không?** Gateway và service phải share cùng `Jwt:Secret`.
5. **Lệch giờ?** `ClockSkew = 30s` mặc định — nếu máy lệch > 30s, sync NTP.

## Bảng lỗi thường gặp

| HTTP | Nguyên nhân | Cách kiểm tra |
|---|---|---|
| 401 | Thiếu `Authorization` header | `curl -v`, đảm bảo `Authorization: Bearer ...` |
| 401 | Sai secret giữa Gateway & service | Diff `Jwt:Secret` ở 2 file appsettings |
| 401 | Sai `Issuer` / `Audience` | Decode token, so với `JwtOptions` |
| 401 | Token hết hạn | Decode → field `exp` |
| 401 | Lệch giờ > 30s | Sync NTP máy |
| 403 | Có token nhưng thiếu policy | Hệ chưa có role policy → chưa gặp |

## Bật log JWT verbose

```json:src/ApiGateway/appsettings.Development.json
"Logging": {
  "LogLevel": {
    "Microsoft.AspNetCore.Authentication": "Debug"
  }
}
```

Log sẽ chỉ rõ:

- `IDX10223: Lifetime validation failed` — hết hạn
- `IDX10500: Signature validation failed` — sai secret
- `IDX10205: Issuer validation failed` — sai issuer

## Liên quan

- [Add authentication](./add-authentication)
- [Explanation: Why Clean Architecture](../explanation/why-clean-architecture)
