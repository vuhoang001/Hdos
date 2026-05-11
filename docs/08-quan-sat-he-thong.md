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

---

## Metrics (Prometheus + prometheus-net)

### Tại sao Prometheus?

Pull-based model: Prometheus chủ động scrape `/metrics` endpoint mỗi 15 giây. Nếu service down → Prometheus biết ngay (scrape fail). Ngược lại với push-based (service tự push): nếu service down, không có gì push → không biết down.

### Cấu hình scrape (`monitoring/prometheus.yml`)
```yaml
global:
  scrape_interval: 15s
  external_labels:
    cluster: hdos

scrape_configs:
  - job_name: prometheus
    static_configs:
      - targets: ['localhost:9090']

  - job_name: authservice
    static_configs:
      - targets: ['authservice:8080']
    metrics_path: /metrics

  - job_name: orderservice
    static_configs:
      - targets: ['orderservice:8080']
    metrics_path: /metrics

  # ... tương tự cho notificationservice, m01service
```

### Metrics được tự động expose

`UseHttpMetrics()` từ thư viện `prometheus-net.AspNetCore` tự động tạo:

```
# Số request mỗi giây
http_requests_total{method, route, status_code}

# Latency histogram
http_request_duration_seconds_bucket{method, route, status_code, le}

# Request đang xử lý
http_requests_in_progress{method, route}
```

**.NET runtime metrics** (GC, memory, threads) được expose tự động bởi `prometheus-net`.

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

# GC heap size
dotnet_gc_heap_size_bytes{generation="loh"}
```

### Thêm custom metric

```csharp
// Ví dụ: đếm số lần login thành công
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
              └── Loki HTTP sink (push tới Grafana Loki)
```

### Cấu hình Serilog (`Common/Logging/LoggingExtensions.cs`)

```csharp
builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.WithProperty("ServiceName", serviceName)
    .Enrich.FromLogContext()                    // Inject RequestId, TraceId, SpanId
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.GrafanaLoki(                       // Chỉ active khi có Loki__Uri env var
        uri: lokiUri,
        labels: [new LokiLabel { Key = "service", Value = serviceName }],
        propertiesAsLabels: ["level"]));
```

**Quan trọng:** `.Enrich.FromLogContext()` inject TraceId/SpanId vào mỗi log entry. `RequestLoggingMiddleware` push TraceId/SpanId vào Serilog's `LogContext` khi xử lý request. Nhờ đó có thể link log → trace trong Grafana.

### Loki config (`monitoring/loki.yml`)
```yaml
schema_config:
  configs:
    - from: 2024-01-01
      store: tsdb
      schema: v13
      index:
        prefix: index_
        period: 24h

limits_config:
  ingestion_rate_mb: 16
  ingestion_burst_size_mb: 32
  max_cache_freshness_per_query: 10m
```

### LogQL mẫu

```logql
# Log của authservice trong 1 giờ qua
{service="authservice"}

# Chỉ error logs
{service="authservice"} |= "level=error"

# Log của request cụ thể (link từ trace)
{service="m01service"} |= "TraceId=abc123def456"

# Count lỗi theo phút
count_over_time({service=~".+"} |= "level=error" [1m])
```

---

## Traces (OpenTelemetry + Tempo)

### Pipeline

```
Service code
  └── ASP.NET Core (tự động trace HTTP requests)
        └── HttpClient (tự động trace outbound calls)
              └── RabbitMQ ActivitySource (manual)
                    └── OpenTelemetry SDK
                          └── OTLP Exporter (gRPC :4317)
                                └── Grafana Tempo
```

### Setup (`Common/Monitoring/OpenTelemetryExtensions.cs`)

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        // Tự động instrument tất cả HTTP request đến
        .AddAspNetCoreInstrumentation(opts =>
        {
            opts.RecordException = true;
            // Bỏ qua /health và /metrics để không làm ồn trace
            opts.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health") &&
                !ctx.Request.Path.StartsWithSegments("/metrics");
        })
        // Tự động instrument HttpClient outbound (gRPC calls)
        .AddHttpClientInstrumentation(opts => opts.RecordException = true)
        // Custom source cho RabbitMQ (manual instrument)
        .AddSource("Hdos.Messaging")
        // Export sang Tempo qua OTLP/gRPC
        .AddOtlpExporter(otlp =>
            otlp.Endpoint = new Uri(configuration["OpenTelemetry:OtlpEndpoint"]!)));
```

### Tempo config (`monitoring/tempo.yml`)
```yaml
server:
  http_listen_port: 3200

distributor:
  receivers:
    otlp:
      protocols:
        grpc:   { endpoint: "0.0.0.0:4317" }  # Nhận traces từ services
        http:   { endpoint: "0.0.0.0:4318" }

storage:
  trace:
    backend: local
    local:
      path: /var/tempo
    wal:
      path: /var/tempo/wal

compactor:
  compaction:
    block_retention: 1h     # Giữ trace 1 giờ (dev) — tăng lên 7d cho prod

metrics_generator:
  processors: [service-graphs, span-metrics]  # Tạo RED metrics từ traces
```

### Grafana Datasources (auto-provision)
```yaml
# monitoring/grafana/provisioning/datasources/datasources.yml
datasources:
  - name: Prometheus
    type: prometheus
    url: http://prometheus:9090

  - name: Loki
    type: loki
    url: http://loki:3100
    jsonData:
      derivedFields:
        - name: TraceID
          matcherRegex: "TraceId=(\\w+)"
          url: "${__value.raw}"
          datasourceUid: tempo    # Click TraceId trong log → mở Tempo trace

  - name: Tempo
    type: tempo
    url: http://tempo:3200
    jsonData:
      tracesToLogs:
        datasourceUid: loki       # Click trace → xem log tương ứng
        tags: [service]
      serviceMap:
        datasourceUid: prometheus
```

**Điều này cho phép:**
1. Thấy error trong Grafana Logs → click TraceId → xem full trace trong Tempo
2. Thấy slow trace trong Tempo → click → xem logs của request đó trong Loki

---

## Grafana Dashboard

Dashboard "Hdos — Service Overview" (auto-provisioned) có các panel:

| Panel | PromQL | Mô tả |
|-------|--------|-------|
| Request Rate | `sum(rate(http_requests_total[5m])) by (job)` | Req/s theo service |
| Error Rate | `rate({5xx}[5m]) / rate(total[5m])` | % lỗi |
| P99 Latency | `histogram_quantile(0.99, ...)` | Latency 99th percentile |
| GC Heap | `dotnet_gc_heap_size_bytes` | Memory usage |
| Active Threads | `dotnet_threadpool_num_threads` | Thread pool |
| Error Logs | Loki panel `{service=~".+"} \|= "Error"` | Log lỗi realtime |

---

## Thêm service mới vào monitoring

1. **Prometheus** (`monitoring/prometheus.yml`):
```yaml
- job_name: newservice
  static_configs:
    - targets: ['newservice:8080']
```

2. **docker-compose.monitoring.yml** — thêm env vars cho service mới:
```yaml
newservice:
  environment:
    - OpenTelemetry__OtlpEndpoint=http://tempo:4317
    - Loki__Uri=http://loki:3100
```

3. Không cần thay đổi Loki hoặc Tempo — chúng nhận tất cả tự động qua push.
