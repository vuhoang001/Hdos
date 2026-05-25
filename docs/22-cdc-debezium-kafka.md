# 22 — CDC với Debezium + Kafka

**Change Data Capture (CDC)** là kỹ thuật bắt mọi thay đổi ở cấp độ DB row (INSERT/UPDATE/DELETE) và stream chúng sang các hệ thống khác theo thời gian thực.

---

## CDC vs Outbox — khi nào dùng cái nào

| | Outbox (doc 21) | CDC (doc này) |
|---|---|---|
| **Bắt sự kiện gì** | Domain event do code chủ động raise | Mọi thay đổi DB row, kể cả migration/script |
| **Granularity** | Business intent (`OrderCreated`) | Data change (`dbo.Orders row updated`) |
| **Infrastructure** | Chỉ cần DB + MassTransit | Cần Kafka + Debezium |
| **Dùng khi** | Service-to-service integration event | Sync sang search index, data warehouse, audit log |

---

## Kiến trúc trong Hdos

```
SQL Server (OrderDb)
    │  Transaction Log
    │  └─ CDC tables (dbo.cdc.Orders_CT)
    ▼
Debezium (Kafka Connect)         ← đọc CDC tables mỗi ~1s
    │  Debezium envelope JSON
    ▼
Kafka Topic: hdos.OrderDb.dbo.Orders
    │
    ├─► NotificationService.OrderCdcConsumer  ← gửi alert, audit log
    └─► [bất kỳ service nào cần react]
```

### Tại sao Debezium đọc CDC table thay vì transaction log trực tiếp?

SQL Server CDC hoạt động khác MySQL/PostgreSQL:
- **MySQL/PostgreSQL**: Debezium đọc binlog/WAL trực tiếp
- **SQL Server**: SQL Server Agent chạy capture jobs → ghi vào `cdc.*` tables → Debezium đọc từ đó

→ **Yêu cầu bắt buộc**: SQL Server Agent phải đang chạy.

---

## Bước 1 — Enable SQL Server Agent

```bash
# Enable SQL Server Agent (chỉ cần 1 lần)
docker exec -it hdos-sqlserver \
  /opt/mssql/bin/mssql-conf set sqlagent.enabled true

docker restart hdos-sqlserver
```

Verify Agent đang chạy:
```sql
SELECT name, status_desc FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server Agent%';
```

---

## Bước 2 — Enable CDC trên SQL Server

```bash
# Chạy script enable CDC
docker exec -i hdos-sqlserver \
  /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Hdos!DevPass123' -C \
  -i /dev/stdin < infra/sql/enable-cdc.sql
```

Verify:
```sql
USE OrderDb;
SELECT t.name, c.capture_instance
FROM cdc.change_tables c
JOIN sys.tables t ON t.object_id = c.source_object_id;
```

---

## Bước 3 — Khởi động Kafka + Debezium

```bash
docker compose -f docker-compose.yml -f docker-compose.kafka.yml up -d
```

Các container mới:
| Container | Port | Mục đích |
|---|---|---|
| `hdos-zookeeper` | — | Kafka coordination |
| `hdos-kafka` | `9092` | Message broker |
| `hdos-kafka-connect` | `8083` | Debezium (Kafka Connect) |
| `hdos-kafka-ui` | `8090` | UI quản lý Kafka |

---

## Bước 4 — Register Debezium connector

```bash
# Đợi kafka-connect healthy rồi chạy
bash infra/debezium/register-connector.sh
```

Verify connector đang chạy:
```bash
curl -s http://localhost:8083/connectors/hdos-orders-connector/status | jq .
```

Response mong muốn:
```json
{
  "name": "hdos-orders-connector",
  "connector": { "state": "RUNNING" },
  "tasks": [{ "state": "RUNNING" }]
}
```

Sau khi register, Debezium thực hiện **initial snapshot** — đọc toàn bộ Orders hiện tại và publish lên Kafka. Sau đó chuyển sang CDC mode, bắt thay đổi theo thời gian thực.

---

## Kafka Topic và message format

**Topic name**: `hdos.OrderDb.dbo.Orders`
(pattern: `{prefix}.{database}.{schema}.{table}`)

**Message key**: primary key của row (dạng JSON)

**Message value** — Debezium envelope:

```json
{
  "payload": {
    "before": null,
    "after": {
      "Id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "CustomerId": "...",
      "CustomerEmail": "alice@hdos.io",
      "Status": 0
    },
    "op": "c",
    "source": {
      "db": "OrderDb",
      "schema": "dbo",
      "table": "Orders",
      "ts_ms": 1716624000000
    },
    "ts_ms": 1716624000001
  }
}
```

| `op` | Ý nghĩa | `before` | `after` |
|---|---|---|---|
| `"c"` | INSERT | `null` | row mới |
| `"u"` | UPDATE | row cũ | row mới |
| `"d"` | DELETE | row cũ | `null` |
| `"r"` | Initial snapshot read | `null` | row hiện tại |

---

## Code .NET — CdcConsumerService

### Base class (Common)

`CdcConsumerService<TEntity>` là abstract `BackgroundService` trong `Hdos.Common.Kafka`:
- Tự build Confluent.Kafka consumer
- Subscribe topic, poll theo vòng lặp
- Deserialize Debezium envelope → gọi `HandleAsync`
- **Manual commit**: chỉ commit offset sau khi `HandleAsync` thành công → at-least-once delivery
- Lỗi handler: không commit, retry từ offset cũ sau restart

### Concrete consumer (NotificationService)

```csharp
// NotificationService.Infrastructure/Cdc/OrderCdcConsumer.cs

public sealed class OrderCdcRow
{
    public string? Id { get; init; }
    public string? CustomerEmail { get; init; }
    public int Status { get; init; }
}

public sealed class OrderCdcConsumer(
    IOptions<KafkaConsumerOptions> options,
    ILogger<OrderCdcConsumer> logger)
    : CdcConsumerService<OrderCdcRow>(logger)
{
    protected override KafkaConsumerOptions Options => options.Value;

    protected override Task HandleAsync(DebeziumPayload<OrderCdcRow> payload, CancellationToken ct)
    {
        switch (payload.Operation)
        {
            case CdcOperation.Created:
                // gửi welcome email, tạo audit log...
                break;
            case CdcOperation.Updated when payload.Before?.Status != payload.After?.Status:
                // status changed → notify customer
                break;
        }
        return Task.CompletedTask;
    }
}
```

### Đăng ký trong DI

```csharp
// NotificationService.Infrastructure/DependencyInjection.cs

var kafkaSection = configuration.GetSection(KafkaConsumerOptions.SectionName);
if (!string.IsNullOrEmpty(kafkaSection["Topic"]))
{
    services.Configure<KafkaConsumerOptions>(kafkaSection);
    services.AddHostedService<OrderCdcConsumer>();
}
```

### Config (appsettings.json)

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "GroupId": "notification-cdc-consumer",
    "Topic": "hdos.OrderDb.dbo.Orders"
  }
}
```

Hoặc qua environment variable trong docker-compose:
```yaml
environment:
  Kafka__BootstrapServers: "kafka:29092"
  Kafka__GroupId: "notification-cdc-consumer"
  Kafka__Topic: "hdos.OrderDb.dbo.Orders"
```

---

## Thêm CDC cho bảng khác

### 1. Enable CDC trên bảng mới

```sql
USE OrderDb;
EXEC sys.sp_cdc_enable_table
    @source_schema = N'dbo',
    @source_name   = N'Products',
    @role_name     = NULL;
```

### 2. Thêm bảng vào Debezium connector

```bash
curl -X PUT http://localhost:8083/connectors/hdos-orders-connector/config \
  -H "Content-Type: application/json" \
  -d '{
    ...existing config...,
    "table.include.list": "dbo.Orders,dbo.OrderItems,dbo.Products"
  }'
```

### 3. Tạo consumer mới kế thừa `CdcConsumerService<TEntity>`

---

## Troubleshooting

### Connector ở trạng thái FAILED

```bash
curl -s http://localhost:8083/connectors/hdos-orders-connector/status | jq .
# Xem task errors

# Restart task
curl -X POST http://localhost:8083/connectors/hdos-orders-connector/tasks/0/restart
```

Nguyên nhân phổ biến:
- SQL Server Agent chưa chạy → CDC tables không được populate
- CDC chưa enable trên database/table
- SQL Server chưa healthy khi connector register

### Consumer không nhận được message

```bash
# Xem messages trong topic (Kafka UI: http://localhost:8090)
# Hoặc dùng CLI:
docker exec -it hdos-kafka \
  kafka-console-consumer \
  --bootstrap-server kafka:29092 \
  --topic hdos.OrderDb.dbo.Orders \
  --from-beginning \
  --max-messages 5
```

### Consumer lag (bị trễ)

```bash
docker exec -it hdos-kafka \
  kafka-consumer-groups \
  --bootstrap-server kafka:29092 \
  --describe \
  --group notification-cdc-consumer
```

---

## Files liên quan

```
docker-compose.kafka.yml                     ← overlay khởi động CDC stack
infra/debezium/
    sqlserver-connector.json                 ← Debezium connector config
    register-connector.sh                    ← script đăng ký connector
infra/sql/
    enable-cdc.sql                           ← enable CDC trên SQL Server

src/BuildingBlocks/Common/Kafka/
    DebeziumEnvelope.cs                      ← Debezium message types
    KafkaConsumerOptions.cs                  ← config options
    CdcConsumerService.cs                    ← abstract base BackgroundService

src/Services/NotificationService/
    NotificationService.Infrastructure/Cdc/
        OrderCdcConsumer.cs                  ← concrete consumer demo
```
