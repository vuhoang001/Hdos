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

| URL | Công cụ | Credentials |
|-----|---------|-------------|
| http://localhost:3030 | Grafana | admin / admin |
| http://localhost:9090 | Prometheus | — |
| http://localhost:3100 | Loki API | — |
| http://localhost:3200 | Tempo API | — |

### Chạy service local + monitoring Docker

Khi chạy `dotnet run` (không qua Docker Compose), thêm vào `appsettings.Development.json`:

```json
{
  "OpenTelemetry": { "OtlpEndpoint": "http://localhost:4317" },
  "Loki":          { "Uri": "http://localhost:3100" }
}
```

Nếu không có monitoring stack, service vẫn hoạt động bình thường — OTLP exporter và Loki sink bị bỏ qua:
```
[WARN] OpenTelemetry:OtlpEndpoint is not configured — traces will not be exported.
```

---

## Metrics (Prometheus + prometheus-net)

### Cấu hình scrape (`monitoring/prometheus.yml`)

```yaml
scrape_configs:
  - job_name: authservice
    static_configs:
      - targets: ['authservice:8080']
        labels: { service: AuthService }
    metrics_path: /metrics

  - job_name: nginx
    static_configs:
      - targets: ['nginx-exporter:9113']
        labels: { service: Nginx }
```

### Metrics được tự động expose

`UseHttpMetrics()` (prometheus-net.AspNetCore) tự động tạo:

```
http_requests_total{method, route, status_code}       # Số request theo thời gian
http_request_duration_seconds_bucket{..., le}         # Latency histogram
http_requests_in_progress{method, route}              # Request đang xử lý
```

`.NET runtime metrics` (GC, memory, threads) được expose tự động.

**Health check metrics** (`Common/HealthChecks/PrometheusHealthPublisher`):
```
hdos_health_check_status{check="sqlserver"}   1   # 1=Healthy, 0=Unhealthy
hdos_health_check_status{check="rabbitmq"}    1
```

**Nginx metrics** (qua `nginx-prometheus-exporter` sidecar):
```
nginx_connections_active          # Kết nối đang xử lý
nginx_http_requests_total         # Tổng request
```

### PromQL mẫu

```promql
# Request rate (req/s) trong 5 phút
rate(http_requests_total{job="authservice"}[5m])

# Error rate (%)
sum(rate(http_requests_total{status_code=~"5.."}[5m]))
/ sum(rate(http_requests_total[5m])) * 100

# Latency P99
histogram_quantile(0.99,
  sum(rate(http_request_duration_seconds_bucket[5m])) by (le, route))

# Service nào đang unhealthy
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
Service code → ILogger<T>.LogInformation(...)
  └── Serilog
        ├── Console sink  (stdout → docker logs)
        └── Loki HTTP sink (push tới Grafana Loki) — chỉ khi Loki:Uri được set

nginx stdout → Promtail (docker socket discovery) → Loki HTTP push
```

### Cấu hình Serilog

```csharp
builder.Host.UseSerilog((ctx, lc) => lc
    .Enrich.FromLogContext()                    // Inject TraceId, SpanId
    .Enrich.WithProperty("Service", serviceName)
    .WriteTo.Console(...)
    .WriteTo.GrafanaLoki(
        uri: lokiUri,
        labels: [new LokiLabel { Key = "service", Value = serviceName }],
        propertiesAsLabels: ["level"]));
```

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
  └── ASP.NET Core (auto: HTTP requests)
        └── HttpClient (auto: outbound calls)
              └── RabbitMQ ActivitySource "Hdos.Messaging" (manual)
                    └── OpenTelemetry SDK
                          └── OTLP Exporter (gRPC :4317) → Grafana Tempo
```

### Setup (`Common/Monitoring/OpenTelemetryExtensions.cs`)

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName))
    .WithTracing(tracing => tracing
        .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
        .AddAspNetCoreInstrumentation(opts => {
            opts.RecordException = true;
            opts.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health") &&
                !ctx.Request.Path.StartsWithSegments("/metrics");
        })
        .AddHttpClientInstrumentation(opts => opts.RecordException = true)
        .AddSource("Hdos.Messaging")   // RabbitMQ spans
        .AddOtlpExporter(...));
```

### Trace Sampling

| Giá trị `SamplingRatio` | Ý nghĩa |
|------------------------|---------|
| `1.0` | 100% traces (default dev) |
| `0.1` | 10% (production nhẹ) |
| `0.01` | 1% (production cao tải) |

`ParentBasedSampler` đảm bảo nếu parent span đã được sample, child span cũng được sample — không cắt đứt trace giữa chừng.

```json
// docker-compose env var override
OpenTelemetry__SamplingRatio: "0.05"
```

---

## W3C Trace Context & Distributed Tracing

### W3C traceparent header

Chuẩn W3C định nghĩa format truyền trace context qua HTTP:

```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
             │  │                                │                │
             │  TraceId (128-bit hex)            SpanId(64-bit)   Flags(01=sampled)
             version
```

**Qua HTTP (tự động):** `AddAspNetCoreInstrumentation` + `AddHttpClientInstrumentation` tự inject/extract header.

```
TraceId: AAAA (bất biến suốt hành trình)
│
├── GET /m01/dashboard [M01Service] 45ms
│   ├── GET /auth/validate [AuthService] 3ms   ← auto via HttpClient
│   └── DB query [M01Service] 38ms
```

### Trace qua RabbitMQ (Manual)

HTTP tự inject header. RabbitMQ AMQP không có standard header — cần inject thủ công vào AMQP message headers.

**Publisher — Inject traceparent vào AMQP headers:**

```csharp
// Common/Messaging/RabbitMqEventBus.cs
private static readonly ActivitySource _activitySource = new("Hdos.Messaging");
private static readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

public Task PublishAsync<TEvent>(TEvent @event, ...)
{
    using var activity = _activitySource.StartActivity(
        $"rabbitmq publish {typeof(TEvent).Name}", ActivityKind.Producer);

    var props = channel.CreateBasicProperties();
    props.Headers = new Dictionary<string, object>();

    // Inject W3C traceparent vào AMQP headers dưới dạng byte[]
    _propagator.Inject(
        new PropagationContext(activity?.Context ?? default, Baggage.Current),
        props.Headers,
        static (headers, key, value) => headers[key] = Encoding.UTF8.GetBytes(value));

    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("messaging.destination", typeof(TEvent).Name);

    channel.BasicPublish(exchange: _options.Exchange,
        routingKey: typeof(TEvent).Name, basicProperties: props, body: body);
}
```

> **Lý do encode thành byte[]:** RabbitMQ AMQP header value type là `object`. Dùng `byte[]` đảm bảo consistent giữa các client.

**Consumer — Extract traceparent và link trace:**

```csharp
// Common/Messaging/RabbitMqConsumerHostedService.cs
private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
{
    // Extract traceparent từ AMQP headers
    var parentContext = _propagator.Extract(default, ea.BasicProperties.Headers,
        static (headers, key) =>
        {
            if (headers?.TryGetValue(key, out var value) == true && value is byte[] bytes)
                return [Encoding.UTF8.GetString(bytes)];
            return [];
        });

    Baggage.Current = parentContext.Baggage;

    // Tạo span linked với producer span — cùng TraceId, cross-service!
    using var activity = _activitySource.StartActivity(
        $"rabbitmq process {ea.RoutingKey}",
        ActivityKind.Consumer,
        parentContext.ActivityContext);

    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("messaging.rabbitmq.queue", _queueName);

    // Push TraceId/SpanId vào Serilog LogContext để log có TraceId
    using var logScope = _logger.BeginScope(new Dictionary<string, object?>
    {
        ["TraceId"] = activity?.TraceId.ToHexString(),
        ["SpanId"]  = activity?.SpanId.ToHexString(),
    });

    try
    {
        var @event = JsonSerializer.Deserialize<TEvent>(ea.Body.ToArray());
        await handler.HandleAsync(@event, CancellationToken.None);
        _channel!.BasicAck(ea.DeliveryTag, multiple: false);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: !ea.Redelivered);
    }
}
```

### Kết quả trong Tempo

Trace đầy đủ khi user đăng nhập lần đầu (JIT provision → publish event):

```
TraceId: 4bf92f3577b34da6a3ce929d0e0e4736
│
├── GET /auth/validate [AuthService] 120ms
│   ├── INSERT User [SQL Server] 8ms
│   └── rabbitmq publish UserRegisteredIntegrationEvent [AuthService] 2ms
│         ── AMQP header traceparent injected ──►
│
└── rabbitmq process UserRegisteredIntegrationEvent [NotificationService] 15ms
    ├── INSERT Notification [SQL Server] 8ms
    └── SignalR push [NotificationService] 1ms
```

**Một TraceId duy nhất** xuyên suốt nhiều services và RabbitMQ.

---

## Log ↔ Trace Correlation

`RequestLoggingMiddleware` inject TraceId/SpanId vào mọi log entry:

```csharp
using (LogContext.PushProperty("TraceId", activity?.TraceId.ToString()))
using (LogContext.PushProperty("SpanId",  activity?.SpanId.ToString()))
{
    await _next(context);
    _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {Elapsed}ms", ...);
}
```

**Grafana datasource config** (auto-provisioned):

```yaml
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
      datasourceUid: prometheus   # Service dependency map
```

Workflow:
1. Thấy error trong Loki → click TraceId → xem full trace trong Tempo
2. Thấy slow trace trong Tempo → click → xem logs của request đó trong Loki

---

## TraceQL — Query mạnh nhất

Vào **Grafana → Explore → Tempo → tab TraceQL**:

```
# Tìm tất cả RabbitMQ spans
{ name =~ "rabbitmq.*" }

# Tìm theo service
{ name =~ "rabbitmq.*" && resource.service.name = "NotificationService" }

# Tìm trace chậm
{ resource.service.name = "OrderService" } | duration > 500ms

# Tìm theo messaging destination
{ span.messaging.destination = "UserRegisteredIntegrationEvent" }
```

---

## Grafana Dashboard

Dashboard "Hdos — Service Overview" (auto-provisioned):

| Panel | Query | Mô tả |
|-------|-------|-------|
| Request Rate | `sum(rate(http_requests_total[5m])) by (job)` | Req/s theo service |
| Error Rate | `rate({5xx}[5m]) / rate(total[5m])` | % lỗi |
| P99 Latency | `histogram_quantile(0.99, ...)` | Latency 99th percentile |
| Health Status | `hdos_health_check_status` | DB/RabbitMQ health |
| Error Logs | `{service=~".+"} \|= "Error"` | Log lỗi realtime |

---

## Thêm service mới vào monitoring

**1. `monitoring/prometheus.yml`** — thêm scrape job:
```yaml
- job_name: newservice
  static_configs:
    - targets: ['newservice:8080']
      labels: { service: NewService }
  metrics_path: /metrics
```

**2. `docker-compose.monitoring.yml`** — inject env vars:
```yaml
newservice:
  environment:
    OpenTelemetry__OtlpEndpoint: http://tempo:4317
    Loki__Uri: http://loki:3100
```

**3. `appsettings.json`** của service mới:
```json
{
  "OpenTelemetry": { "OtlpEndpoint": "", "SamplingRatio": 1.0 },
  "Loki": { "Uri": "" }
}
```

**4. `appsettings.Development.json`** — local dev:
```json
{
  "OpenTelemetry": { "OtlpEndpoint": "http://localhost:4317" },
  "Loki": { "Uri": "http://localhost:3100" }
}
```

Loki và Tempo không cần cấu hình thêm — nhận tất cả tự động qua push.
