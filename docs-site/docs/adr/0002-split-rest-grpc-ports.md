---
title: ADR-0002 — Tách port REST và gRPC trong cùng service
sidebar_position: 4
description: Chạy REST/HTTP1 và gRPC/HTTP2 trên 2 port Kestrel khác nhau thay vì cùng 1 port.
tags: [adr, accepted, networking, grpc]
---

# ADR-0002 — Tách port REST và gRPC trong cùng service

- **Status:** <span className="badge-adr badge-adr-accepted">Accepted</span>
- **Date:** 2026-05-08
- **Deciders:** @hoanggggf
- **Tags:** `networking`, `grpc`, `kestrel`

## Context

`AuthService` cần expose 2 protocol:

- **REST + Swagger UI** cho client public (Gateway forward) — yêu cầu HTTP/1.1.
- **gRPC** cho service-to-service (OrderService gọi `GetUserById`) — yêu cầu HTTP/2 over plaintext (h2c).

Kestrel có thể serve cả 2 trên cùng 1 port nếu config `HttpProtocols.Http1AndHttp2`, nhưng:

- Swashbuckle Swagger UI prefer HTTP/1.1
- gRPC server không tương thích với HTTPS termination ở reverse proxy mặc định
- Cấu hình ALPN cho cùng 1 port khá khó debug

## Decision

> **Mỗi service expose 2 port Kestrel riêng**: 1 cho REST (HTTP/1.1+2), 1 cho gRPC (HTTP/2 only).

```csharp:src/Services/AuthService/AuthService.API/Program.cs
builder.WebHost.ConfigureKestrel(options =>
{
    var restPort = builder.Configuration.GetValue<int>("Kestrel:RestPort", 8080);
    var grpcPort = builder.Configuration.GetValue<int>("Kestrel:GrpcPort", 8081);
    options.ListenAnyIP(restPort, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
    options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
});
```

Mặc định:

- AuthService: REST `5101`, gRPC `5111`
- Trong container: `8080` / `8081`, override qua env `Kestrel__RestPort` / `Kestrel__GrpcPort`

## Consequences

### Positive ✅

- Cấu hình đơn giản, không phải fiddle ALPN
- Có thể firewall riêng port gRPC (chỉ mở cho service-to-service trong cluster)
- Swagger UI hoạt động ngay không cần config thêm

### Negative ❌

- 1 service = 2 port → phải document, monitoring tool phải biết cả 2
- docker-compose phải expose 2 port cho mỗi service

### Neutral ⚖️

- Service nào không có gRPC (Order, Notification) thì chỉ có REST port — lộ port pattern không đồng đều

## Alternatives considered

### Alt A — Cùng 1 port cho cả REST + gRPC

- Cấu hình `HttpProtocols.Http1AndHttp2` + ALPN
- **Vì sao không chọn**: debug khó, Swagger UI có vấn đề với một số config
- Reference: https://learn.microsoft.com/aspnet/core/grpc/aspnetcore#configure-kestrel

### Alt B — Tách AuthService.gRPC thành service riêng

- **Vì sao không chọn**: deploy thêm 1 process chỉ để serve gRPC là overkill cho scale hiện tại

## References

- [ASP.NET Core gRPC docs](https://learn.microsoft.com/aspnet/core/grpc)
- [HTTP/2 ALPN negotiation](https://datatracker.ietf.org/doc/html/rfc7301)
- [C4 Component — AuthService](../explanation/c4/component-auth)
