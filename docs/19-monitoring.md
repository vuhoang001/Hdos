# 19 — Monitoring & Observability

Hệ thống observability của Hdos bám theo **three pillars**:

| Pillar | Công nghệ | Lưu trữ |
|--------|-----------|---------|
| **Logs** | Serilog → HTTP push | Grafana Loki |
| **Metrics** | prometheus-net → scrape | Prometheus |
| **Traces** | OpenTelemetry OTLP → push | Grafana Tempo |

Tất cả có thể xem trên **Grafana** duy nhất ở `http://localhost:3000`.

---

## 1. Yêu cầu hệ thống

| Phụ thuộc | Phiên bản tối thiểu | Ghi chú |
|-----------|---------------------|---------|
| Docker Engine | 24.0+ | `docker --version` |
| Docker Compose | v2.20+ | `docker compose version` |
| Git | bất kỳ | clone repo |
| RAM trống | ≥ 4 GB | SQL Server + monitoring stack |
| Ports trống | xem bảng dưới | — |

**Ports cần trống trên máy host:**

| Port | Service |
|------|---------|
| 5000 | API Gateway |
| 1433 | SQL Server |
| 5672 / 15672 | RabbitMQ / RabbitMQ UI |
| 9090 | Prometheus |
| 3000 | Grafana |
| 3100 | Loki |
| 3200 | Tempo HTTP |
| 4317 / 4318 | Tempo OTLP gRPC / HTTP |

---

## 2. Cài đặt lần đầu (Quick Start)

### Bước 1 — Clone repo

```bash
git clone <repo-url>
cd Hdos
```

### Bước 2 — Tạo file `.env` (tuỳ chọn)

Mặc định hệ thống dùng `JWT_SECRET` mặc định cho dev. Để override:

```bash
echo "JWT_SECRET=your-secret-min-32-chars-here" > .env
```

> Nếu không tạo `.env`, hệ thống vẫn chạy với secret mặc định (chỉ dùng cho dev).

### Bước 3 — Chạy toàn bộ hệ thống (app + monitoring)

```bash
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

Lần đầu chạy sẽ mất **3–5 phút** để Docker pull images. Các lần sau rất nhanh.

### Bước 4 — Kiểm tra tất cả services đã lên

```bash
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml ps
```

Tất cả phải có status `Up` hoặc `Up (healthy)`. Nếu có service nào `Restarting`, xem log:

```bash
docker compose logs <tên-service>
# Ví dụ:
docker compose logs authservice
```

### Bước 5 — Kiểm tra health

```bash
# Gateway
curl http://localhost:5000/health/ready

# Các service qua gateway
curl http://localhost:5000/orders/health/ready
curl http://localhost:5000/notifications/health/ready
curl http://localhost:5000/m01/health/ready
```

Kết quả mong đợi:
```json
{"status":"Healthy","totalDuration":12.5,"checks":[
  {"name":"sqlserver","status":"Healthy","durationMs":5.2,"tags":["db"]},
  {"name":"rabbitmq","status":"Healthy","durationMs":1.1,"tags":["messaging"]}
]}
```

### Bước 6 — Mở Grafana

Truy cập `http://localhost:3000`, đăng nhập `admin / admin`.

Vào **Dashboards → Hdos → Hdos — Service Overview** để xem dashboard tự động provision.

---

## 3. Kiến trúc observability

```
┌─────────────────────────────────────────────────────────────────┐
│                     Application Services                         │
│  ApiGateway │ AuthService │ OrderService │ Notification │ M01    │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Serilog                                                   │  │
│  │    Console sink ─────────────────────────► stdout         │  │
│  │    Loki sink (nếu Loki__Uri được set) ───► Loki :3100     │  │
│  │    TraceId/SpanId tự động inject vào log context          │  │
│  ├────────────────────────────────────────────────────────────┤  │
│  │  prometheus-net                                            │  │
│  │    UseHttpMetrics() ─── middleware track requests         │  │
│  │    GET /metrics ──────────────────────────────────────────┼──┼─► Prometheus pull
│  ├────────────────────────────────────────────────────────────┤  │
│  │  OpenTelemetry (traces only)                              │  │
│  │    OTLP export (nếu OtlpEndpoint được set) ─► Tempo :4317│  │
│  ├────────────────────────────────────────────────────────────┤  │
│  │  Health Checks                                            │  │
│  │    GET /health/live  ── liveness (luôn 200)              │  │
│  │    GET /health/ready ── readiness (check DB + RabbitMQ)  │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
           │ push                    │ push             │ scrape
           ▼                         ▼                  ▼
      Grafana Loki             Grafana Tempo        Prometheus
      (log store)              (trace store)        (metric store)
           │                         │                  │
           └─────────────────────────┼──────────────────┘
                                     ▼
                              Grafana :3000
                          (logs + traces + metrics)
```

---

## 4. Chỉ chạy app (không monitoring)

Khi chỉ cần chạy app để dev, không cần Prometheus/Grafana:

```bash
docker compose up -d
```

Khi đó:
- Logs vẫn xuất ra console bình thường
- `/metrics` vẫn expose nhưng không có ai scrape
- Traces không được export (không có Tempo)
- Health checks vẫn hoạt động

---

## 5. Các lệnh thường dùng

```bash
# Dừng tất cả (giữ data volume)
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml down

# Dừng + xóa toàn bộ data (reset sạch)
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml down -v

# Xem logs realtime của 1 service
docker compose logs -f authservice

# Xem logs monitoring
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml logs -f grafana

# Restart 1 service sau khi sửa code
docker compose build authservice && docker compose up -d authservice

# Rebuild tất cả
docker compose build && docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d

# Scale 1 service lên 2 instance (load balancing YARP)
docker compose up -d --scale orderservice=2
```

---

## 6. Truy cập các UI

| Service | URL | Credentials | Ghi chú |
|---------|-----|-------------|---------|
| API Gateway | http://localhost:5000 | — | Entry point chính |
| Swagger (aggregated) | http://localhost:5000/swagger | — | Chỉ bật khi Development |
| Grafana | http://localhost:3000 | admin / admin | Dashboard chính |
| Prometheus | http://localhost:9090 | — | Xem targets, query raw metrics |
| Prometheus Targets | http://localhost:9090/targets | — | Kiểm tra scrape status |
| Loki | http://localhost:3100/ready | — | Health check Loki |
| Tempo | http://localhost:3200/ready | — | Health check Tempo |
| RabbitMQ UI | http://localhost:15672 | guest / guest | Quản lý queues, exchanges |

---

## 7. Cấu hình trong code

Toàn bộ monitoring được tập trung trong `BuildingBlocks/Common/`. Mỗi service
chỉ cần gọi 3 dòng trong `Program.cs`:

```csharp
// 1. Logging (Console luôn bật + Loki khi có env var)
builder.UseHdosLogging("AuthService");

// 2. Metrics + Traces
builder.Services.AddHdosOpenTelemetry(builder.Configuration, "AuthService");

// 3. Health Checks
builder.Services.AddHdosHealthChecks(builder.Configuration,
    sqlConnectionStringKey: "AuthDb",   // null = bỏ qua SQL check
    checkRabbitMq: true);               // false = bỏ qua RabbitMQ check

// Sau app.Build()...
app.UseHdosMonitoring(); // maps /metrics + /health/live + /health/ready
```

### 7.1 Logging — `SerilogConfig.cs`

```
SerilogConfig.UseHdosLogging(serviceName)
  ├── Console sink         — luôn bật, format: [HH:mm:ss LVL] [Service] Message {json}
  └── Loki sink            — chỉ bật khi Loki__Uri có trong config
       Labels: service=AuthService, environment=Development, level=Error
```

`TraceId` và `SpanId` từ OpenTelemetry **tự động được đính** vào mọi log entry
trong một HTTP request (thông qua `RequestLoggingMiddleware` + Serilog `LogContext`).
Nhờ đó Grafana có thể link từ log entry → trace trong Tempo.

**Env var kích hoạt Loki:**
```
Loki__Uri=http://loki:3100
```

### 7.2 Distributed Tracing — `OpenTelemetryExtensions.cs`

```
AddHdosOpenTelemetry(serviceName)
  └── WithTracing
       ├── AddAspNetCoreInstrumentation  — mọi HTTP request tạo 1 span
       ├── AddHttpClientInstrumentation  — mọi outbound HTTP call tạo span con
       ├── Filter: bỏ qua /health + /metrics
       └── AddOtlpExporter               — chỉ bật khi OtlpEndpoint có trong config
```

**Env var kích hoạt traces:**
```
OpenTelemetry__OtlpEndpoint=http://tempo:4317
```

Khi không set env var: app chạy bình thường, không có traces (không lỗi).

### 7.3 Metrics — `prometheus-net` (WebApplicationExtensions.cs)

```
UseHdosMiddleware()
  └── UseHttpMetrics()   — middleware của prometheus-net, track mọi HTTP request

UseHdosMonitoring()
  └── MapMetrics()       — expose /metrics endpoint cho Prometheus scrape
```

Metrics được expose **luôn luôn** (không cần env var). Prometheus scrapes từ
internal Docker network `hdos-net`, không cần expose port ra host.

**Metrics tự động có:**

| Metric | Type | Mô tả |
|--------|------|-------|
| `http_request_duration_seconds` | histogram | Duration + count theo code, method, endpoint |
| `http_requests_in_progress` | gauge | Số request đang xử lý |
| `dotnet_total_memory_bytes` | gauge | Tổng GC heap |
| `dotnet_collection_count_total` | counter | GC collections theo generation |
| `process_working_set_bytes` | gauge | RAM process đang dùng |
| `process_num_threads` | gauge | Số thread |
| `process_cpu_seconds_total` | counter | CPU time |

### 7.4 Health Checks — `HealthCheckExtensions.cs`

| Endpoint | Dùng cho | Logic |
|----------|----------|-------|
| `GET /health/live` | Kubernetes liveness probe | Luôn trả `Healthy` (app còn chạy) |
| `GET /health/ready` | Kubernetes readiness probe | Check SQL Server + RabbitMQ thật sự |

Cách gọi qua Gateway (có path transform YARP):
```
GET /orders/health/ready  →  orderservice:8080/health/ready
GET /auth/health/ready    →  authservice:8080/health/ready
```

---

## 8. Metrics quan trọng (PromQL)

Dán thẳng vào **Grafana → Explore → Prometheus** hoặc `http://localhost:9090`.

### HTTP Traffic

```promql
# Request rate toàn hệ thống (req/s)
sum(rate(http_request_duration_seconds_count[$__rate_interval]))

# Request rate theo từng service
sum by(job) (rate(http_request_duration_seconds_count[$__rate_interval]))

# Tỉ lệ lỗi 5xx (%)
sum(rate(http_request_duration_seconds_count{code=~"5.."}[$__rate_interval]))
  / sum(rate(http_request_duration_seconds_count[$__rate_interval]))

# Latency P50 / P95 / P99 toàn hệ thống
histogram_quantile(0.50, sum by(le) (rate(http_request_duration_seconds_bucket[$__rate_interval])))
histogram_quantile(0.95, sum by(le) (rate(http_request_duration_seconds_bucket[$__rate_interval])))
histogram_quantile(0.99, sum by(le) (rate(http_request_duration_seconds_bucket[$__rate_interval])))

# Latency P95 theo service
histogram_quantile(0.95,
  sum by(le, job) (rate(http_request_duration_seconds_bucket[$__rate_interval]))
)

# Số request đang in-flight
sum(http_requests_in_progress)

# Top endpoints chậm nhất
topk(5,
  histogram_quantile(0.95,
    sum by(le, endpoint) (rate(http_request_duration_seconds_bucket[$__rate_interval]))
  )
)
```

### .NET Runtime

```promql
# Tổng heap size theo service (bytes)
dotnet_total_memory_bytes

# GC collections rate (gen0 = minor, gen2 = full GC)
sum by(job, generation) (rate(dotnet_collection_count_total[$__rate_interval]))

# RAM usage (working set) — MB
process_working_set_bytes / 1024 / 1024

# CPU usage (%)
rate(process_cpu_seconds_total[$__rate_interval]) * 100

# Thread count
process_num_threads
```

### Alerts tham khảo

```promql
# Error rate > 5% trong 5 phút
sum(rate(http_request_duration_seconds_count{code=~"5.."}[5m]))
  / sum(rate(http_request_duration_seconds_count[5m])) > 0.05

# P95 latency > 1 giây trong 5 phút
histogram_quantile(0.95,
  sum by(le) (rate(http_request_duration_seconds_bucket[5m]))
) > 1

# GC Gen2 collections tăng nhanh
rate(dotnet_collection_count_total{generation="2"}[5m]) > 0.1
```

---

## 9. Log queries (LogQL — Loki)

Dán vào **Grafana → Explore → Loki**.

```logql
# Tất cả logs của AuthService
{service="AuthService"}

# Chỉ lấy lỗi
{service="AuthService"} |= "ERR"

# Parse JSON để filter theo field
{environment="Development"} | json | level="Error"

# Lọc theo HTTP status code
{service="OrderService"} | json | StatusCode="500"

# Tìm trace cụ thể (cross-link với Tempo)
{service="OrderService"} | json | TraceId="<trace-id-ở-đây>"

# Log rate theo service (số dòng/phút)
sum by(service) (rate({environment="Development"}[1m]))

# Lỗi nhiều nhất trong 10 phút qua
topk(10,
  sum by(service) (count_over_time({environment="Development"} |= "ERR" [10m]))
)
```

---

## 10. Distributed Tracing — Grafana Tempo

### Tìm trace trong Grafana
1. Mở `http://localhost:3000`
2. Vào **Explore → chọn datasource Tempo**
3. Tìm theo **Service Name** (dropdown) → Search
4. Click vào trace để xem waterfall diagram
5. Click **"View Logs"** để nhảy sang Loki và xem logs cùng TraceId

### Từ log → trace
1. Mở Loki Explore
2. Query: `{service="AuthService"} | json`
3. Click vào log entry có `TraceId`
4. Click link **"View Trace"** → mở Tempo với trace đó

### Service Map
Vào **Grafana → Explore → Tempo → chọn tab "Service Map"**.
Hiển thị topology của hệ thống và RED metrics (Rate, Error, Duration) giữa các service.

### TraceId trong log
Mọi log trong một HTTP request đều có `TraceId` và `SpanId` nhờ
`RequestLoggingMiddleware` đẩy vào Serilog `LogContext`:
```
[14:30:01 INF] [AuthService] HTTP POST /auth/login responded 200 in 45ms
  {"TraceId":"1f2e3d4c5b6a7890","SpanId":"abcdef1234567890","Service":"AuthService",...}
```

---

## 11. Grafana Dashboard

Dashboard **"Hdos — Service Overview"** được tự động provision khi Grafana khởi
động từ `monitoring/grafana/provisioning/`.

### Panels trong dashboard

| Row | Panel | Metric |
|-----|-------|--------|
| HTTP Traffic | Total req/s | `http_request_duration_seconds_count` |
| HTTP Traffic | Error rate % | `code=~"5.."` filter |
| HTTP Traffic | P95 Latency | histogram_quantile 0.95 |
| HTTP Traffic | In-flight | `http_requests_in_progress` |
| HTTP Traffic | Req/s by service | group by `job` |
| HTTP Traffic | Latency by service | p50/p95/p99 |
| .NET Runtime | GC heap | `dotnet_total_memory_bytes` |
| .NET Runtime | GC collections | `dotnet_collection_count_total` |
| .NET Runtime | Threads | `process_num_threads` |
| .NET Runtime | RAM | `process_working_set_bytes` |
| Logs | Error logs | Loki: `\|= "ERR"` |

### Import thêm community dashboards

1. Vào **Grafana → Dashboards → Import**
2. Nhập ID từ grafana.com/dashboards:

| ID | Tên | Ghi chú |
|----|-----|---------|
| `19004` | ASP.NET Core + prometheus-net | Khớp chính xác với setup này |
| `13659` | Loki Logs Dashboard | Overview logs toàn hệ thống |
| `15760` | Tempo / Tracing | Trace analytics |

---

## 12. Thêm custom metrics vào service

Dùng API của `prometheus-net` (không cần thêm package — đã có qua `prometheus-net.AspNetCore`):

```csharp
using Prometheus;

// Khai báo ở class level (static — tạo 1 lần)
private static readonly Counter OrdersCreated = Metrics
    .CreateCounter("orders_created_total", "Total orders created",
        new CounterConfiguration { LabelNames = new[] { "status" } });

private static readonly Histogram OrderDuration = Metrics
    .CreateHistogram("order_processing_seconds", "Order processing duration");

// Dùng trong business logic
public async Task<Result<OrderDto>> Handle(CreateOrderCommand command, CancellationToken ct)
{
    using var timer = OrderDuration.NewTimer();  // tự stop khi ra khỏi using block
    try
    {
        var order = // ...create order
        OrdersCreated.WithLabels("success").Inc();
        return Result.Ok(order.ToDto());
    }
    catch (Exception)
    {
        OrdersCreated.WithLabels("failure").Inc();
        throw;
    }
}
```

Metrics này **tự động xuất hiện** trong `/metrics` endpoint và Prometheus sẽ
scrape. Không cần cấu hình gì thêm.

**PromQL để query custom metric:**
```promql
# Tổng orders được tạo theo service
sum by(job) (orders_created_total)

# Rate tạo orders thành công trong 5 phút
sum(rate(orders_created_total{status="success"}[5m]))

# P95 thời gian xử lý order
histogram_quantile(0.95, rate(order_processing_seconds_bucket[5m]))
```

---

## 13. Troubleshooting

### Metrics không xuất hiện trong Prometheus

```bash
# 1. Kiểm tra service đang chạy
docker compose ps

# 2. Kiểm tra Prometheus scrape status
# Mở http://localhost:9090/targets — tất cả phải "UP"

# 3. Kiểm tra /metrics trực tiếp trên gateway
curl http://localhost:5000/metrics | head -20

# 4. Xem logs service để tìm lỗi startup
docker compose logs authservice | tail -30
```

### Traces không xuất hiện trong Tempo

```bash
# 1. Kiểm tra Tempo đang chạy và healthy
curl http://localhost:3200/ready

# 2. Kiểm tra env var đã được set khi chạy với monitoring compose
docker inspect hdos-authservice-1 | grep -A5 "OpenTelemetry"
# Phải thấy: "OpenTelemetry__OtlpEndpoint=http://tempo:4317"

# 3. Kiểm tra Tempo nhận được data
curl "http://localhost:3200/api/search?limit=5"

# 4. Xem logs Tempo
docker logs hdos-tempo --tail 20
```

> **Lưu ý:** Traces chỉ được export khi chạy với monitoring compose:
> `docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d`
> Khi dùng `docker compose up -d` đơn thuần thì không có Tempo → không có traces (không lỗi).

### Logs không xuất hiện trong Loki

```bash
# 1. Kiểm tra Loki ready
curl http://localhost:3100/ready
# Expected: "ready"

# 2. Kiểm tra env var
docker inspect hdos-authservice-1 | grep Loki
# Phải thấy: "Loki__Uri=http://loki:3100"

# 3. Query thử trong Loki Explore
# {service="AuthService"} — nếu không có kết quả, logs chưa push lên

# 4. Xem logs Loki để tìm ingestion error
docker logs hdos-loki --tail 20
```

### Service không start được (DB migration fail)

```bash
# Xem log để biết lỗi cụ thể
docker compose logs authservice | grep -i "error\|retry"

# Thường gặp: SQL Server chưa kịp ready
# Fix: chờ 30s rồi restart service
docker compose restart authservice
```

### Grafana không load datasources

```bash
# 1. Kiểm tra provisioning files được mount đúng
docker exec hdos-grafana ls /etc/grafana/provisioning/datasources/
# Expected: datasources.yml

# 2. Restart Grafana
docker restart hdos-grafana

# 3. Xem logs
docker logs hdos-grafana | grep -i "error\|provisioning"
```

### Reset toàn bộ (xóa data)

```bash
# Dừng và xóa tất cả containers + volumes
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml down -v

# Chạy lại từ đầu
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

---

## 14. File structure

```
Hdos/
├── docker-compose.yml                      # App services (dev)
├── docker-compose.prod.yml                 # App services (prod — GHCR images)
├── docker-compose.monitoring.yml           # Monitoring overlay (Prometheus/Loki/Tempo/Grafana)
│
├── monitoring/
│   ├── prometheus.yml                      # Scrape config: 5 services + self
│   ├── loki.yml                            # Loki storage + schema config
│   ├── tempo.yml                           # Tempo OTLP receivers + storage
│   └── grafana/
│       └── provisioning/
│           ├── datasources/
│           │   └── datasources.yml         # Prometheus + Loki + Tempo auto-config
│           └── dashboards/
│               ├── dashboard-provider.yml  # Dashboard loader
│               └── hdos-overview.json      # Main overview dashboard
│
└── src/BuildingBlocks/Common/
    ├── Monitoring/
    │   └── OpenTelemetryExtensions.cs      # AddHdosOpenTelemetry() — traces only
    ├── HealthChecks/
    │   ├── HealthCheckExtensions.cs        # AddHdosHealthChecks() + /health/* endpoints
    │   └── RabbitMqHealthCheck.cs          # Custom check dùng RabbitMqConnection
    ├── Logging/
    │   └── SerilogConfig.cs                # UseHdosLogging() — Console + Loki sink
    ├── Extensions/
    │   └── WebApplicationExtensions.cs     # UseHdosMonitoring() — /metrics + health
    └── Middleware/
        └── RequestLoggingMiddleware.cs     # Inject TraceId/SpanId vào log context
```

---

## 15. Quyết định thiết kế (ADR)

**Tại sao dùng `prometheus-net` thay OTel Prometheus exporter?**

`OpenTelemetry.Exporter.Prometheus.AspNetCore` vẫn đang ở trạng thái pre-release
(beta). Phiên bản 1.10.0-beta.1 kéo theo dependency `Microsoft.Extensions.* 9.0.0`
gây version conflict với .NET 8 framework. `prometheus-net 8.x` là thư viện
Prometheus native cho .NET, stable, battle-tested, và không có vấn đề này.

Trade-off: metric names khác với OTel convention (`http_request_duration_seconds`
thay vì `http_server_request_duration_seconds`). Khi OTel Prometheus exporter
stable, có thể switch lại bằng cách thay `prometheus-net.AspNetCore` →
`OpenTelemetry.Exporter.Prometheus.AspNetCore` và cập nhật PromQL queries.

**Tại sao không dùng OpenTelemetry Collector?**

Collector thêm một service nữa vào stack và phức tạp hóa config. Với quy mô
hiện tại (5 services), direct export từ app → Tempo (traces) và Prometheus pull
→ `/metrics` (metrics) là đủ và đơn giản hơn.
