# 08 — Quan sát hệ thống (Observability)

Microservices không thể debug bằng cách nhìn vào một log file duy nhất. Cần ba trụ cột:

| Trụ cột | Công cụ | Trả lời câu hỏi |
|---------|---------|----------------|
| **Metrics** | Prometheus → Grafana | "Hệ thống đang làm gì?" (số liệu theo thời gian) |
| **Logs** | Serilog → Loki → Grafana | "Chuyện gì đã xảy ra?" (text có context) |
| **Traces** | OpenTelemetry → Tempo → Grafana | "Request đi qua đâu, chậm ở bước nào?" |

---

## Khởi động monitoring

```bash
docker compose -f docker-compose.yml -f docker-compose.monitoring.yml up -d
```

| URL | Công cụ |
|-----|---------|
| http://localhost:3030 | Grafana (admin/admin) |
| http://localhost:9090 | Prometheus |
| http://localhost:3100 | Loki API |
| http://localhost:3200 | Tempo API |

### Chạy service local + monitoring Docker

Khi chạy `dotnet run` (không qua Docker Compose), mỗi service tự đọc endpoint từ `appsettings.Development.json`:

```json
{
  "OpenTelemetry": { "OtlpEndpoint": "http://localhost:4317" },
  "Loki":          { "Uri": "http://localhost:3100" }
}
```

Traces và logs tự động được gửi đến monitoring stack đang chạy trong Docker. Nếu không có monitoring stack, service vẫn hoạt động bình thường — OTLP exporter và Loki sink chỉ bị bỏ qua, có warning xuất hiện trong console:

```
[WARN][AuthService] OpenTelemetry:OtlpEndpoint is not configured — traces will not be exported.
```

---

## Metrics (Prometheus + prometheus-net)

### Tại sao Prometheus?

Pull-based model: Prometheus chủ động scrape `/metrics` endpoint mỗi 15 giây. Nếu service down → Prometheus biết ngay (scrape fail). Ngược lại với push-based: nếu service down, không có gì push → không biết down.

### Cấu hình scrape (`monitoring/prometheus.yml`)

```yaml
# NAMING CONVENTION: job_name phải khớp tên service trong docker-compose.yml.
# Khi thêm service mới: thêm job ở đây + env-inject block trong docker-compose.monitoring.yml.
scrape_configs:
  - job_name: authservice
    static_configs:
      - targets: ['authservice:8080']
        labels: { service: AuthService }
    metrics_path: /metrics

  - job_name: nginx          # Nginx gateway — qua nginx-prometheus-exporter
    static_configs:
      - targets: ['nginx-exporter:9113']
        labels: { service: Nginx }
```

### Metrics được tự động expose

`UseHttpMetrics()` từ thư viện `prometheus-net.AspNetCore` tự động tạo:

```
http_requests_total{method, route, status_code}        # Số request mỗi giây
http_request_duration_seconds_bucket{..., le}          # Latency histogram
http_requests_in_progress{method, route}               # Request đang xử lý
```

**.NET runtime metrics** (GC, memory, threads) được expose tự động bởi `prometheus-net`.

### Health check metrics

`PrometheusHealthPublisher` (trong `Common/HealthChecks/`) publish kết quả health check vào `/metrics` mỗi 15 giây:

```
hdos_health_check_status{check="sqlserver"}   1   # 1=Healthy, 0=Unhealthy
hdos_health_check_status{check="rabbitmq"}    1
```

Alert example trong Prometheus/Grafana:
```promql
hdos_health_check_status == 0
```

### Nginx metrics (nginx-prometheus-exporter)

nginx không tự expose Prometheus metrics. Stack dùng `nginx-prometheus-exporter` làm sidecar:

```
nginx (port 8081 stub_status) → nginx-exporter:9113/metrics → Prometheus
```

Metrics chính:
```
nginx_connections_active          # Kết nối đang xử lý
nginx_http_requests_total         # Tổng request
nginx_connections_accepted_total  # Tổng kết nối được chấp nhận
```

### PromQL mẫu

```promql
# Request rate (req/s) trong 5 phút qua
rate(http_requests_total{job="authservice"}[5m])

# Error rate (%)
sum(rate(http_requests_total{status_code=~"5.."}[5m]))
/ sum(rate(http_requests_total[5m])) * 100

# Latency P99
histogram_quantile(0.99,
  sum(rate(http_request_duration_seconds_bucket[5m])) by (le, route))

# Service nào đang có dependency unhealthy
hdos_health_check_status == 0
```

### Thêm custom metric

```csharp
private static readonly Counter _loginCounter = Metrics
    .CreateCounter("auth_login_success_total", "Number of successful logins",
        new CounterConfiguration { LabelNames = ["user_type"] });

// Trong handler:
_loginCounter.WithLabels("regular").Inc();
```

---

## Logs (Serilog + Loki)

### Pipeline

```
Service code
  └── ILogger<T>.LogInformation(...)
        └── Serilog
              ├── Console sink (stdout → docker logs)
              └── Loki HTTP sink (push tới Grafana Loki) — chỉ khi Loki:Uri được set
```

```
nginx container stdout
  └── Promtail (docker socket discovery)
        └── Loki HTTP push
```

### Cấu hình Serilog (`Common/Logging/SerilogConfig.cs`)

```csharp
builder.Host.UseSerilog((ctx, lc) => lc
    .Enrich.FromLogContext()                    // Inject TraceId, SpanId từ RequestLoggingMiddleware
    .Enrich.WithProperty("Service", serviceName)
    .WriteTo.Console(...)
    .WriteTo.GrafanaLoki(                       // Chỉ active khi Loki:Uri != ""
        uri: lokiUri,
        labels: [new LokiLabel { Key = "service", Value = serviceName }],
        propertiesAsLabels: ["level"]));
```

### Nginx logs (Promtail)

`monitoring/promtail.yml` dùng Docker socket để tự động discover container `hdos-nginx` và push access logs vào Loki với label `{service="nginx"}`. Parsed fields: `status`, `method` thành Loki labels để filter nhanh.

### LogQL mẫu

```logql
# Log của authservice
{service="authservice"}

# Chỉ error logs
{service="authservice"} | json | level = "error"

# Nginx 5xx errors
{service="nginx"} | status =~ "5.."

# Log của request cụ thể (link từ trace)
{service="m01service"} |= "abc123def456"

# Count lỗi theo phút
count_over_time({service=~".+"} |= "Error" [1m])
```

---

## Traces (OpenTelemetry + Tempo)

### Pipeline

```
Service code
  └── ASP.NET Core (tự động trace HTTP requests)
        └── HttpClient (tự động trace outbound calls)
              └── RabbitMQ ActivitySource (manual — xuyên message queue)
                    └── OpenTelemetry SDK
                          └── OTLP Exporter (gRPC :4317)
                                └── Grafana Tempo
```

### Setup (`Common/Monitoring/OpenTelemetryExtensions.cs`)

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
        .AddAspNetCoreInstrumentation(opts =>
        {
            opts.RecordException = true;
            opts.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health") &&
                !ctx.Request.Path.StartsWithSegments("/metrics");
        })
        .AddHttpClientInstrumentation(opts => opts.RecordException = true)
        .AddSource("Hdos.Messaging")       // RabbitMQ spans
        .AddOtlpExporter(...));            // Export sang Tempo qua OTLP/gRPC
```

### Trace sampling

Sampling được điều khiển qua `OpenTelemetry:SamplingRatio` trong appsettings:

| Giá trị | Ý nghĩa |
|---------|---------|
| `1.0` | Sample 100% traces (default dev) |
| `0.1` | Sample 10% (production nhẹ) |
| `0.01` | Sample 1% (production cao tải) |

`ParentBasedSampler` đảm bảo nếu parent span đã được sample, child span cũng được sample — không cắt đứt trace giữa chừng.

```json
// appsettings.json — base (production override)
{ "OpenTelemetry": { "SamplingRatio": 0.1 } }

// docker-compose env var override
OpenTelemetry__SamplingRatio: "0.05"
```

### Tempo config (`monitoring/tempo.yml`)

```yaml
compactor:
  compaction:
    block_retention: 24h    # Dev default. Production: set TEMPO_BLOCK_RETENTION=7d

metrics_generator:
  processors: [service-graphs, span-metrics]  # Tạo RED metrics từ traces
```

### Grafana Datasources (auto-provision)

```yaml
datasources:
  - name: Loki
    jsonData:
      derivedFields:
        - name: TraceID
          matcherRegex: '"TraceId":"(\w+)"'  # Click TraceId trong log → mở Tempo trace
          datasourceUid: tempo

  - name: Tempo
    jsonData:
      tracesToLogs:
        datasourceUid: loki         # Click trace → xem logs tương ứng
      serviceMap:
        datasourceUid: prometheus   # Service dependency map từ Tempo metrics generator
```

Điều này cho phép:
1. Thấy error trong Grafana Logs → click TraceId → xem full trace trong Tempo
2. Thấy slow trace trong Tempo → click → xem logs của request đó trong Loki

---

## Trace-to-Log Correlation

`RequestLoggingMiddleware` inject TraceId/SpanId vào mọi log entry trong request:

```csharp
using (LogContext.PushProperty("TraceId", activity.TraceId.ToString()))
using (LogContext.PushProperty("SpanId", activity.SpanId.ToString()))
{
    await _next(context); // Mọi ILogger.Log() trong scope này đều có TraceId/SpanId
}
```

Grafana extract TraceId từ log fields và tạo link → Tempo.

---

## Grafana Dashboard

Dashboard "Hdos — Service Overview" (auto-provisioned) có các panel:

| Panel | Query | Mô tả |
|-------|-------|-------|
| Request Rate | `sum(rate(http_requests_total[5m])) by (job)` | Req/s theo service |
| Error Rate | `rate({5xx}[5m]) / rate(total[5m])` | % lỗi |
| P99 Latency | `histogram_quantile(0.99, ...)` | Latency 99th percentile |
| Health Status | `hdos_health_check_status` | DB/RabbitMQ health |
| Error Logs | Loki: `{service=~".+"} \|= "Error"` | Log lỗi realtime |

---

## Thêm service mới vào monitoring

1. **`monitoring/prometheus.yml`** — thêm scrape job:
```yaml
- job_name: newservice              # Phải khớp tên service trong docker-compose.yml
  static_configs:
    - targets: ['newservice:8080']
      labels: { service: NewService }
  metrics_path: /metrics
```

2. **`docker-compose.monitoring.yml`** — inject env vars:
```yaml
newservice:
  environment:
    OpenTelemetry__OtlpEndpoint: http://tempo:4317
    Loki__Uri: http://loki:3100
```

3. **`appsettings.json`** của service mới — khai báo sections:
```json
{
  "OpenTelemetry": { "OtlpEndpoint": "", "SamplingRatio": 1.0 },
  "Loki": { "Uri": "" }
}
```

4. **`appsettings.Development.json`** — local dev endpoints:
```json
{
  "OpenTelemetry": { "OtlpEndpoint": "http://localhost:4317" },
  "Loki": { "Uri": "http://localhost:3100" }
}
```

Không cần thay đổi Loki hoặc Tempo — chúng nhận tất cả tự động qua push.
