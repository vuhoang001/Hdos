# 20 — Cấu hình môi trường trên Server (Production / Staging)

## Tổng quan

Stack Hdos trên server cần **hai lớp cấu hình** tách biệt:

| Lớp | File | Mục đích |
|-----|------|----------|
| **Compose substitution** | `${ENV_DIR}/.env` | Biến dùng trong YAML (`image:`, `MSSQL_SA_PASSWORD`, ...) |
| **Container env** | `common.env`, `*.service.env` | Biến inject thẳng vào process bên trong container |

> **Tại sao cần tách?** Docker Compose có hai cơ chế riêng biệt:
> - `--env-file` → thay thế `${VAR}` trong file YAML trước khi tạo container
> - `env_file:` trong service → inject biến vào môi trường container lúc runtime

---

## Cấu trúc thư mục trên server

```
/opt/hdos-prod/          (hoặc /opt/hdos-staging/)
├── .env                 ← compose substitution, đọc bởi --env-file
├── common.env           ← inject vào tất cả services + sqlserver
├── authservice.env
├── orderservice.env
├── notificationservice.env
├── m01service.env
└── apigateway.env
```

---

## Nội dung từng file

### `/opt/hdos-prod/.env`

Dùng bởi lệnh `docker compose --env-file`. Cung cấp giá trị cho phép thay thế
`${VAR}` trong `docker-compose.yml` và `docker-compose.server.yml`.

```env
IMAGE_TAG=prod-latest
GHCR_OWNER=vuhoang001
ASPNETCORE_ENVIRONMENT=Production
ENV_DIR=/opt/hdos-prod
MSSQL_SA_PASSWORD=<mật_khẩu_sql_server>
JWT_SECRET=<chuỗi_ngẫu_nhiên_>=32_ký_tự>
```

> `MSSQL_SA_PASSWORD` ở đây phải **trùng khớp 100%** với giá trị trong `common.env`.
> Docker Compose đọc file này để thay thế `${MSSQL_SA_PASSWORD}` trong YAML rồi
> truyền vào container `sqlserver`.

### `/opt/hdos-prod/common.env`

Inject vào tất cả application services và sqlserver. Mỗi biến phải trên **một dòng
duy nhất**, không có leading spaces, không xuống dòng giữa giá trị.

```env
# Phải khớp với MSSQL_SA_PASSWORD trong .env
MSSQL_SA_PASSWORD=<mật_khẩu_sql_server>

ConnectionStrings__AuthDb=Server=sqlserver,1433;Database=AuthDb;User Id=sa;Password=<mật_khẩu_sql_server>;TrustServerCertificate=True;Encrypt=False
ConnectionStrings__OrderDb=Server=sqlserver,1433;Database=OrderDb;User Id=sa;Password=<mật_khẩu_sql_server>;TrustServerCertificate=True;Encrypt=False
ConnectionStrings__NotificationDb=Server=sqlserver,1433;Database=NotificationDb;User Id=sa;Password=<mật_khẩu_sql_server>;TrustServerCertificate=True;Encrypt=False
ConnectionStrings__M01Db=Server=sqlserver,1433;Database=M01Db;User Id=sa;Password=<mật_khẩu_sql_server>;TrustServerCertificate=True;Encrypt=False

RabbitMq__Host=rabbitmq
RabbitMq__Port=5672

Jwt__Secret=<jwt_secret_>=32_ký_tự>
Jwt__Issuer=Hdos.Auth
Jwt__Audience=Hdos.Services
Jwt__ExpiresMinutes=60
```

> **Lỗi format phổ biến:** Nếu dùng editor tự xuống dòng (word-wrap), giá trị sẽ bị
> cắt thành nhiều dòng → Docker Compose parse sai → biến không được nhận.
> Kiểm tra với `cat -A common.env` — mỗi dòng phải kết thúc bằng `$` (ký hiệu newline).

### `/opt/hdos-prod/authservice.env`

```env
Kestrel__RestPort=8080
Kestrel__GrpcPort=8081
```

### `/opt/hdos-prod/orderservice.env`

```env
Services__Auth__GrpcUrl=http://authservice:8081
```

### Các file còn lại

```bash
# Tạo file rỗng nếu service chưa cần biến riêng
touch /opt/hdos-prod/notificationservice.env
touch /opt/hdos-prod/m01service.env
touch /opt/hdos-prod/apigateway.env
```

---

## Tạo cấu trúc từ đầu (server mới)

```bash
# 1. Tạo thư mục
sudo mkdir -p /opt/hdos-prod
sudo chown ubuntu:ubuntu /opt/hdos-prod
chmod 700 /opt/hdos-prod

# 2. Sinh mật khẩu an toàn
DB_PASS=$(openssl rand -base64 18 | tr -d '=+/' | head -c 24)
JWT=$(openssl rand -base64 48)
echo "DB pass: $DB_PASS"
echo "JWT    : $JWT"

# 3. Ghi .env
cat > /opt/hdos-prod/.env << EOF
IMAGE_TAG=prod-latest
GHCR_OWNER=vuhoang001
ASPNETCORE_ENVIRONMENT=Production
ENV_DIR=/opt/hdos-prod
MSSQL_SA_PASSWORD=${DB_PASS}
JWT_SECRET=${JWT}
EOF
chmod 600 /opt/hdos-prod/.env

# 4. Ghi common.env
cat > /opt/hdos-prod/common.env << EOF
MSSQL_SA_PASSWORD=${DB_PASS}
ConnectionStrings__AuthDb=Server=sqlserver,1433;Database=AuthDb;User Id=sa;Password=${DB_PASS};TrustServerCertificate=True;Encrypt=False
ConnectionStrings__OrderDb=Server=sqlserver,1433;Database=OrderDb;User Id=sa;Password=${DB_PASS};TrustServerCertificate=True;Encrypt=False
ConnectionStrings__NotificationDb=Server=sqlserver,1433;Database=NotificationDb;User Id=sa;Password=${DB_PASS};TrustServerCertificate=True;Encrypt=False
ConnectionStrings__M01Db=Server=sqlserver,1433;Database=M01Db;User Id=sa;Password=${DB_PASS};TrustServerCertificate=True;Encrypt=False
RabbitMq__Host=rabbitmq
RabbitMq__Port=5672
Jwt__Secret=${JWT}
Jwt__Issuer=Hdos.Auth
Jwt__Audience=Hdos.Services
Jwt__ExpiresMinutes=60
EOF
chmod 600 /opt/hdos-prod/common.env

# 5. Ghi service env
cat > /opt/hdos-prod/authservice.env << 'EOF'
Kestrel__RestPort=8080
Kestrel__GrpcPort=8081
EOF

cat > /opt/hdos-prod/orderservice.env << 'EOF'
Services__Auth__GrpcUrl=http://authservice:8081
EOF

touch /opt/hdos-prod/notificationservice.env
touch /opt/hdos-prod/m01service.env
touch /opt/hdos-prod/apigateway.env

chmod 600 /opt/hdos-prod/*.env
```

---

## Chạy stack thủ công trên server

Khi cần deploy thủ công hoặc debug (thay `Hdos/Hdos` bằng thư mục checkout của bạn):

```bash
cd /home/ubuntu/actions-runner/_work/Hdos/Hdos

# Pull image mới
docker compose \
  --env-file /opt/hdos-prod/.env \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  pull

# Khởi động (hoặc restart)
docker compose \
  --env-file /opt/hdos-prod/.env \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  up -d --remove-orphans
```

> **Bắt buộc dùng `--env-file`**: thiếu flag này, `${MSSQL_SA_PASSWORD}` trong YAML
> sẽ không được thay thế → sqlserver nhận password sai → unhealthy → toàn bộ services
> stuck ở trạng thái `Created`.

---

## Xử lý sự cố

### SQL Server `unhealthy`, services bị `Created` (không start)

**Nguyên nhân:** Mật khẩu trong `.env` / `common.env` không khớp với password đã
lưu trong Docker volume `hdos_hdos-sqldata`.

**Cách fix:**

```bash
# 1. Dừng và xóa tất cả containers
docker compose \
  --env-file /opt/hdos-prod/.env \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  down

# 2. Xóa volume SQL Server (mất data — chỉ làm ở môi trường chấp nhận được)
docker volume rm hdos_hdos-sqldata

# 3. Khởi động lại
docker compose \
  --env-file /opt/hdos-prod/.env \
  -f docker-compose.yml \
  -f docker-compose.server.yml \
  up -d
```

> Nếu **không muốn mất data**, thay bước 2 bằng lệnh đổi password trực tiếp:
> ```bash
> docker exec -it hdos-sqlserver /opt/mssql-tools18/bin/sqlcmd \
>   -S localhost -U sa -P 'OldPassword' -C \
>   -Q "ALTER LOGIN sa WITH PASSWORD = 'NewPassword'"
> ```

### `common.env` parse sai (biến trống)

Kiểm tra format:

```bash
cat -A /opt/hdos-prod/common.env
```

Mỗi dòng phải có dạng `KEY=VALUE$` (dấu `$` là newline, không có space trước `$`).
Nếu thấy dấu space thừa hoặc dòng bị ngắt, xóa và ghi lại từ template ở trên.

### Kiểm tra nhanh toàn bộ stack

```bash
# Xem trạng thái
docker ps --format "table {{.Names}}\t{{.Status}}" | grep hdos

# Test API Gateway
curl http://localhost:5000/health

# Log service cụ thể
docker logs hdos-authservice-1 --tail 30
docker logs hdos-sqlserver --tail 20
```

---

## Lưu ý bảo mật

- Không bao giờ commit nội dung `.env` hoặc `common.env` lên git (đã có trong `.gitignore`)
- Mật khẩu SQL Server phải **đủ mạnh**: chữ hoa + thường + số + ký tự đặc biệt,
  tối thiểu 8 ký tự (yêu cầu của SQL Server)
- JWT Secret tối thiểu 32 ký tự; dùng `openssl rand -base64 48` để sinh
- File `.env` và `common.env` nên có permission `600` (chỉ owner đọc được)
