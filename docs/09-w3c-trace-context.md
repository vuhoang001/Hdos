# 09 — W3C Trace Context

Distributed tracing giải quyết bài toán: một request từ client đi qua nhiều services — làm sao biết toàn bộ hành trình và thời gian từng bước?

---

## W3C Trace Context là gì?

Một chuẩn (W3C Recommendation) định nghĩa format truyền trace context qua HTTP header:

```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
             │  │                                │                │
             │  TraceId (128-bit hex)            SpanId(64-bit)   Flags
             version
```

- **TraceId**: ID duy nhất cho toàn bộ request flow (xuyên suốt nhiều services)
- **SpanId**: ID của operation hiện tại trong service hiện tại
- **Flags**: `01` = sampled (ghi lại), `00` = not sampled

---

## Trace qua HTTP (Tự động)

Được xử lý hoàn toàn bởi `AddAspNetCoreInstrumentation` và `AddHttpClientInstrumentation`:

```
Browser                nginx              M01Service          (không trace nginx)
   │                     │                    │
   │ GET /m01/dashboard   │                    │
   │─────────────────────►│                    │
   │                      │  GET /m01/dashboard │
   │                      │  traceparent: 00-AAAA-BBBB-01
   │                      │───────────────────►│
   │                      │                    │ Span: "GET /m01/dashboard"
   │                      │                    │ TraceId=AAAA, SpanId=CCCC
   │                      │                    │ ParentSpanId=BBBB
```

Khi M01Service gọi gRPC sang AuthService (`auth_request` qua nginx):
```
M01Service                         AuthService
    │                                  │
    │ (auto: HttpClient instrument)     │
    │ GET http://authservice/auth/validate
    │ traceparent: 00-AAAA-CCCC-01     │
    │──────────────────────────────────►│
    │                                  │ Span: "GET /auth/validate"
    │                                  │ TraceId=AAAA (cùng trace!)
    │                                  │ SpanId=DDDD, ParentSpanId=CCCC
```

**Kết quả:** Grafana Tempo hiển thị waterfall:
```
TraceId: AAAA
├── GET /m01/dashboard [M01Service] 45ms
│   ├── GET /auth/validate [AuthService] 3ms   ← auth_request subrequest
│   └── DB query [M01Service] 38ms
```

---

## Trace qua RabbitMQ (Manual)

HTTP có thể tự động inject header. RabbitMQ AMQP không có standard header — cần inject thủ công vào AMQP message headers.

### Publisher: Inject trace context

```csharp
// Common/Messaging/RabbitMqEventBus.cs
private static readonly ActivitySource _activitySource = new("Hdos.Messaging");
private static readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

public Task PublishAsync<TEvent>(TEvent @event, ...)
{
    var routingKey = typeof(TEvent).Name;

    // Tạo Span mới với kind = Producer (OpenTelemetry semantic conventions)
    using var activity = _activitySource.StartActivity(
        $"rabbitmq publish {routingKey}",
        ActivityKind.Producer);

    var props = channel.CreateBasicProperties();
    props.Headers = new Dictionary<string, object>();

    // Inject W3C traceparent/tracestate vào AMQP headers
    // DefaultTextMapPropagator biết format W3C (traceparent header)
    _propagator.Inject(
        new PropagationContext(
            activity?.Context ?? Activity.Current?.Context ?? default,
            Baggage.Current),
        props.Headers,
        // Callback: key = "traceparent", value = "00-{traceId}-{spanId}-01"
        static (headers, key, value) => headers[key] = Encoding.UTF8.GetBytes(value));

    // Gán semantic tags theo OpenTelemetry messaging conventions
    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("messaging.destination", routingKey);
    activity?.SetTag("messaging.destination_kind", "topic");

    channel.BasicPublish(exchange: _options.Exchange, routingKey: routingKey,
        basicProperties: props, body: body);
}
```

**Lý do encode thành byte[]:** RabbitMQ AMQP headers value type là `object`. Một số client serialize string khác nhau. Dùng `byte[]` đảm bảo consistent.

### Consumer: Extract và link trace context

```csharp
// Common/Messaging/RabbitMqConsumerHostedService.cs
private static readonly ActivitySource _activitySource = new("Hdos.Messaging");
private static readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
{
    // Extract traceparent từ AMQP headers
    var parentContext = _propagator.Extract(
        default,
        ea.BasicProperties.Headers,
        static (headers, key) =>
        {
            if (headers is not null &&
                headers.TryGetValue(key, out var value) &&
                value is byte[] bytes)
                return [Encoding.UTF8.GetString(bytes)];   // Decode byte[] → string
            return [];
        });

    // Khôi phục Baggage (key-value context propagation)
    Baggage.Current = parentContext.Baggage;

    // Tạo Span mới linked với producer Span
    // ActivityKind.Consumer = "tôi đang xử lý message từ queue"
    using var activity = _activitySource.StartActivity(
        $"rabbitmq process {ea.RoutingKey}",
        ActivityKind.Consumer,
        parentContext.ActivityContext);  // ← Parent = producer's span context

    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("messaging.destination", ea.RoutingKey);
    activity?.SetTag("messaging.rabbitmq.queue", _queueName);

    try
    {
        var @event = JsonSerializer.Deserialize<TEvent>(ea.Body.ToArray());
        await handler.HandleAsync(@event, CancellationToken.None);
        _channel!.BasicAck(ea.DeliveryTag, multiple: false);
    }
    catch (Exception ex)
    {
        // Đánh dấu span là lỗi — hiện màu đỏ trong Tempo
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: !ea.Redelivered);
    }
}
```

---

## Kết quả trong Grafana Tempo

Với W3C propagation qua RabbitMQ, một trace đầy đủ khi user register:

```
TraceId: 4bf92f3577b34da6a3ce929d0e0e4736
│
├── POST /auth/register [AuthService] 120ms
│   ├── RegisterUserCommandHandler [AuthService] 95ms
│   │   ├── SELECT user by email [SQL Server] 5ms
│   │   └── INSERT user [SQL Server] 8ms
│   └── rabbitmq publish UserRegisteredIntegrationEvent [AuthService] 2ms
│       ↑ traceparent injected here
│
└── rabbitmq process UserRegisteredIntegrationEvent [NotificationService] 15ms
    ↑ linked via extracted traceparent
    ├── INSERT notification [SQL Server] 8ms
    └── SignalR push [NotificationService] 1ms
```

Tất cả là **một TraceId** — dù request đi qua 2 services và RabbitMQ.

---

## ActivitySource "Hdos.Messaging"

```csharp
// Định nghĩa trong cả publisher và consumer (cùng tên)
private static readonly ActivitySource _activitySource = new("Hdos.Messaging");

// Đăng ký trong OpenTelemetry setup:
tracing.AddSource("Hdos.Messaging");
```

Nếu không `AddSource("Hdos.Messaging")`, các span từ source này không được ghi lại — dù `StartActivity()` vẫn chạy (Activity.Current sẽ là null).

---

## Log ↔ Trace linking

`RequestLoggingMiddleware` push TraceId/SpanId vào Serilog LogContext:

```csharp
using (LogContext.PushProperty("TraceId", activity?.TraceId.ToString()))
using (LogContext.PushProperty("SpanId", activity?.SpanId.ToString()))
{
    await _next(context);
    _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {Elapsed}ms",
        method, path, statusCode, elapsed);
}
```

Grafana Loki datasource config `derivedFields` tự động detect TraceId trong log và tạo link sang Tempo:

```
Log entry:
  [INF] HTTP POST /auth/login responded 200 in 83ms
        TraceId=4092180ceb8cd9cb65a0658d5ac7cc12

→ Click TraceId → mở Tempo trace 4092180ceb8cd9cb65a0658d5ac7cc12
```

---

## Sampling

Hiện tại: **100% sampled** (tất cả request đều được trace). OK cho dev/staging.

Production với nhiều traffic: cần giảm xuống 10-20%:
```csharp
tracing.SetSampler(new TraceIdRatioBasedSampler(0.1)); // 10%
```

Hoặc dùng ParentBased sampler (giữ nguyên quyết định của parent):
```csharp
tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)));
```
