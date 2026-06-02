# PROMPT_TEMPLATES.md — Daily Prompt Templates for Hdos Codebase

> Copy template → điền placeholder → paste vào Claude Code.
> Placeholder dạng `[CHỮ_HOA]`. Đừng để sót placeholder chưa điền.

---

## Template 1 — Tạo Endpoint Mới

### Template

```
Thêm một API endpoint mới vào **[TÊN_SERVICE]** (ví dụ: AuthService, OrderService, NotificationService, M01Service, DataMatchingService, DynamicFormService).

**Endpoint:**
- Method: [GET | POST | PUT | DELETE | PATCH]
- Route: `/[ROUTE_PATH]` (ví dụ: `/auth/users/{id}/licenses`)
- Controller: `[TÊN_CONTROLLER]Controller` trong `[TÊN_SERVICE].API/Controllers/`

**Request:**
[MÔ_TẢ_INPUT — body JSON, route param, query string]
Ví dụ:
```json
{
  "field1": "string",
  "field2": 0
}
```

**Response (200 OK):**
[MÔ_TẢ_OUTPUT — shape của DTO trả về]

**Business rule:**
- [QUY_TẮC_1]
- [QUY_TẮC_2]

**Authorization:**
- Permission cần thiết: `[PERMISSION_STRING]` (lấy từ `HdosPermissions` trong BuildingBlocks/Common/Auth/HdosPermissions.cs)
- Ví dụ: `"orders:create"`, `"users:manage"`, hoặc anonymous nếu không cần auth

**Side effects (nếu có):**
- [ ] Raise domain event: `[TÊN_DOMAIN_EVENT]`
- [ ] Publish integration event: `[TÊN_INTEGRATION_EVENT]` sang service `[SERVICE_NHẬN]`
- [ ] Không có side effect

**Yêu cầu:**
Chỉ tạo/sửa file trong `[TÊN_SERVICE].Domain/`, `[TÊN_SERVICE].Application/`, `[TÊN_SERVICE].Infrastructure/`, `[TÊN_SERVICE].API/`.
Hỏi tôi trước khi sửa nhiều hơn 3 file cùng lúc.
Follow đúng pattern Clean Architecture + CQRS đang có trong codebase (xem CLAUDE.md).
```

---

### Ví dụ điền sẵn — Thêm endpoint lấy danh sách license của user

```
Thêm một API endpoint mới vào **AuthService**.

**Endpoint:**
- Method: GET
- Route: `/auth/users/{userId}/licenses`
- Controller: `UsersController` trong `AuthService.API/Controllers/`

**Request:**
- Route param: `userId` (Guid)

**Response (200 OK):**
```json
[
  {
    "id": "guid",
    "plan": "enterprise",
    "modules": ["m01", "analytics"],
    "expiresAtUtc": "2026-12-31T23:59:59Z",
    "isActive": true
  }
]
```

**Business rule:**
- Chỉ trả về license của đúng user được request (không lấy của user khác).
- Nếu userId không tồn tại thì trả 404 với `Error.NotFound("User.NotFound", "User không tồn tại")`.

**Authorization:**
- Permission cần thiết: `"users:manage"` (`HdosPermissions.UsersManage`)

**Side effects:**
- Không có side effect

**Yêu cầu:**
Chỉ tạo/sửa file trong AuthService.Domain/, AuthService.Application/, AuthService.Infrastructure/, AuthService.API/.
Hỏi tôi trước khi sửa nhiều hơn 3 file cùng lúc.
Follow đúng pattern Clean Architecture + CQRS đang có trong codebase (xem CLAUDE.md).
```

---

## Template 2 — Tạo Feature/Usecase Mới (Command hoặc Query)

### Template

```
Tạo một **[Command | Query]** mới trong **[TÊN_SERVICE]**.

**Tên feature:** `[TÊN_ACTION][TÊN_ENTITY][Command | Query]`
Ví dụ: `ChangeUserPasswordCommand`, `GetOrdersByCustomerQuery`

**Thư mục đặt file:**
`src/Services/[TÊN_SERVICE]/[TÊN_SERVICE].Application/Features/[TÊN_FEATURE]/`

**Input (`[TÊN_ACTION][TÊN_ENTITY][Command|Query].cs`):**
```csharp
public sealed record [TÊN_ACTION][TÊN_ENTITY][Command|Query](
    [FIELD_1_TYPE] [Field1],
    [FIELD_2_TYPE] [Field2]
) : IRequest<Result<[RETURN_TYPE]>>;
```

**Output:** `Result<[RETURN_TYPE]>` — trả `[TÊN_DTO]` nếu thành công, `Error` nếu thất bại

**Logic trong Handler:**
1. [BƯỚC_1 — VD: Validate input, kiểm tra entity tồn tại]
2. [BƯỚC_2 — VD: Thực hiện domain operation]
3. [BƯỚC_3 — VD: Lưu xuống DB, raise event nếu có]
4. [BƯỚC_4 — VD: Trả về DTO]

**Validation rules (`[TÊN]Validator.cs`):**
- `[FIELD_1]`: [RULE — VD: NotEmpty, MaxLength(100), EmailAddress]
- `[FIELD_2]`: [RULE]

**Domain event (nếu có):**
- Raise `[TÊN_DOMAIN_EVENT]` trong entity sau khi thực hiện operation

**Integration event (nếu có):**
- Sau khi handle domain event, publish `[TÊN_INTEGRATION_EVENT]` đến RabbitMQ
- Contract đặt tại: `src/BuildingBlocks/Contracts/IntegrationEvents/[TÊN_INTEGRATION_EVENT].cs`

**Yêu cầu:**
- Tạo đủ 3 file: `[Tên]Command.cs`, `[Tên]CommandHandler.cs`, `[Tên]CommandValidator.cs`
- Handler trả `Result<T>`, không ném exception cho lỗi nghiệp vụ
- Không sửa file nào ngoài phạm vi feature này
```

---

### Ví dụ điền sẵn — Đổi mật khẩu user trong AuthService

```
Tạo một **Command** mới trong **AuthService**.

**Tên feature:** `ChangeUserPasswordCommand`

**Thư mục đặt file:**
`src/Services/AuthService/AuthService.Application/Features/ChangePassword/`

**Input (`ChangeUserPasswordCommand.cs`):**
```csharp
public sealed record ChangeUserPasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword
) : IRequest<Result>;
```

**Output:** `Result` (không có value) — thành công thì `Result.Success()`, thất bại thì `Result.Failure(Error...)`

**Logic trong Handler:**
1. Load user từ `IUserRepository` theo `UserId`, trả `Error.NotFound` nếu không có.
2. Dùng `IPasswordHasher<User>` verify `CurrentPassword` với hash hiện tại; trả `Error.Validation("Auth.WrongPassword")` nếu sai.
3. Gọi `user.ChangePassword(newPasswordHash)` — domain method trên entity `User`.
4. `IUserRepository.UpdateAsync(user)` + `IUnitOfWork.SaveChangesAsync()`.

**Validation rules (`ChangeUserPasswordValidator.cs`):**
- `UserId`: `NotEmpty()`
- `CurrentPassword`: `NotEmpty()`
- `NewPassword`: `NotEmpty()`, `MinimumLength(8)`, `Matches("[A-Z]")`, `Matches("[0-9]")`

**Domain event:** Không cần raise event cho operation này.

**Integration event:** Không cần.

**Yêu cầu:**
- Tạo đủ 3 file: `ChangeUserPasswordCommand.cs`, `ChangeUserPasswordCommandHandler.cs`, `ChangeUserPasswordCommandValidator.cs`
- Handler trả `Result`, không ném exception cho lỗi nghiệp vụ
- Không sửa file nào ngoài phạm vi feature này
```

---

## Template 3 — Fix Bug

### Template

```
Fix bug trong **[TÊN_SERVICE]**.

**Mô tả bug:**
[MÔ_TẢ_NGẮN — 1 câu tóm tắt vấn đề]

**Triệu chứng:**
- Input: [MÔ_TẢ_INPUT gây ra bug]
- Expected: [HÀNH_VI_ĐÚNG phải xảy ra]
- Actual: [HÀNH_VI_SAI đang xảy ra — lỗi gì, response code nào, exception message nào]

**Môi trường:**
- Service: [TÊN_SERVICE]
- Endpoint/Feature: `[HTTP_METHOD] /[ROUTE]` hoặc `[TÊN_COMMAND|QUERY]`
- Nghi ngờ file liên quan:
  - `src/Services/[TÊN_SERVICE]/[TÊN_SERVICE].[LAYER]/[PATH_FILE]`

**Log / Stack trace (nếu có):**
```
[DÁN_LOG_HOẶC_STACK_TRACE_VÀO_ĐÂY]
```

**Reproduction steps:**
1. [BƯỚC_1]
2. [BƯỚC_2]
3. [BƯỚC_3]

**Yêu cầu:**
- Chỉ sửa code liên quan trực tiếp đến bug, không refactor xung quanh.
- Giải thích root cause trước khi sửa.
- Nếu cần sửa hơn 3 file thì hỏi tôi trước.
```

---

### Ví dụ điền sẵn — OrderService không publish event sau khi tạo order

```
Fix bug trong **OrderService**.

**Mô tả bug:**
Order được tạo thành công trong DB nhưng `OrderCreatedIntegrationEvent` không bao giờ được publish lên RabbitMQ.

**Triệu chứng:**
- Input: `POST /orders` với body hợp lệ, JWT có permission `orders:create`
- Expected: Order lưu vào DB + `OrderCreatedIntegrationEvent` xuất hiện trên RabbitMQ exchange → NotificationService nhận được
- Actual: Order lưu vào DB thành công (200 OK), nhưng NotificationService không nhận được event nào; kiểm tra RabbitMQ Management UI thấy exchange trống

**Môi trường:**
- Service: OrderService
- Endpoint/Feature: `POST /orders` → `CreateOrderCommand`
- Nghi ngờ file liên quan:
  - `src/Services/OrderService/OrderService.Application/Features/CreateOrder/CreateOrderCommandHandler.cs`
  - `src/Services/OrderService/OrderService.Domain/Entities/Order.cs`
  - `src/Services/OrderService/OrderService.Infrastructure/Persistence/Interceptors/DomainEventPublishingInterceptor.cs`

**Log / Stack trace:**
```
[Không có exception — lệnh SaveChanges thành công nhưng event không được dispatch]
```

**Reproduction steps:**
1. Chạy `docker-compose up` (full stack)
2. Đăng nhập lấy JWT: `POST /auth/login`
3. Gọi `POST /orders` với JWT hợp lệ
4. Kiểm tra RabbitMQ UI tại `http://localhost:15672` — exchange `order-created` không có message
5. Kiểm tra DB OrderDb — bảng Orders có record mới

**Yêu cầu:**
- Chỉ sửa code liên quan trực tiếp đến bug, không refactor xung quanh.
- Giải thích root cause trước khi sửa.
- Nếu cần sửa hơn 3 file thì hỏi tôi trước.
```

---

## Template 4 — Thêm Integration Giữa 2 Service

### Template

```
Thêm integration event flow giữa **[SERVICE_PRODUCER]** và **[SERVICE_CONSUMER]**.

**Trigger (phía Producer):**
- Khi nào event được publish: [MÔ_TẢ_ĐIỀU_KIỆN — VD: "sau khi FormTemplate được publish thành công"]
- Service publish: **[SERVICE_PRODUCER]**
- Tên Integration Event: `[TÊN_INTEGRATION_EVENT]`
  (đặt tại `src/BuildingBlocks/Contracts/IntegrationEvents/[TÊN_INTEGRATION_EVENT].cs`)

**Payload của event:**
```csharp
public sealed record [TÊN_INTEGRATION_EVENT](
    [FIELD_1_TYPE] [Field1],
    [FIELD_2_TYPE] [Field2]
) : IntegrationEvent;
```

**Consumer (phía Consumer):**
- Service nhận: **[SERVICE_CONSUMER]**
- Handler class: `[TÊN_INTEGRATION_EVENT]Handler`
  (đặt tại `src/Services/[SERVICE_CONSUMER]/[SERVICE_CONSUMER].Application/EventHandlers/`)
- Logic handler:
  1. [BƯỚC_1]
  2. [BƯỚC_2]

**Đăng ký consumer:**
- Thêm consumer vào MassTransit config trong `[SERVICE_CONSUMER].Infrastructure` hoặc `[SERVICE_CONSUMER].API/Program.cs`
- Consumer endpoint name theo kebab-case (MassTransit tự format nếu dùng `SetKebabCaseEndpointNameFormatter`)

**Files cần tạo/sửa:**
1. **[Mới]** `src/BuildingBlocks/Contracts/IntegrationEvents/[TÊN_INTEGRATION_EVENT].cs`
2. **[Mới]** `src/Services/[SERVICE_CONSUMER]/[SERVICE_CONSUMER].Application/EventHandlers/[TÊN_INTEGRATION_EVENT]Handler.cs`
3. **[Sửa]** `src/Services/[SERVICE_PRODUCER]/[SERVICE_PRODUCER].Application/EventHandlers/[TÊN_DOMAIN_EVENT]Handler.cs` — thêm publish integration event
4. **[Sửa]** `src/Services/[SERVICE_CONSUMER]/[SERVICE_CONSUMER].Infrastructure/` — đăng ký consumer với MassTransit

**Yêu cầu:**
- Contract (IntegrationEvent record) phải nằm trong BuildingBlocks/Contracts, không được đặt trong service.
- Hỏi tôi trước khi sửa hơn 3 file cùng lúc.
- Không sửa logic hiện có của producer hay consumer ngoài phạm vi task này.
```

---

### Ví dụ điền sẵn — DynamicFormService publish event khi form được submit, NotificationService gửi thông báo

```
Thêm integration event flow giữa **DynamicFormService** và **NotificationService**.

**Trigger (phía Producer):**
- Khi nào event được publish: Sau khi `FormSubmission` được tạo thành công (form được submit bởi user)
- Service publish: **DynamicFormService**
- Tên Integration Event: `FormSubmittedIntegrationEvent`
  (đặt tại `src/BuildingBlocks/Contracts/IntegrationEvents/FormSubmittedIntegrationEvent.cs`)

**Payload của event:**
```csharp
public sealed record FormSubmittedIntegrationEvent(
    Guid SubmissionId,
    Guid FormTemplateId,
    string FormTitle,
    Guid SubmittedByUserId,
    string SubmittedByEmail,
    DateTimeOffset SubmittedAtUtc
) : IntegrationEvent;
```

**Consumer (phía Consumer):**
- Service nhận: **NotificationService**
- Handler class: `FormSubmittedIntegrationEventHandler`
  (đặt tại `src/Services/NotificationService/NotificationService.Application/EventHandlers/FormSubmittedIntegrationEventHandler.cs`)
- Logic handler:
  1. Tạo `Notification` entity với Channel = Push, nội dung "Form '[FormTitle]' đã được submit thành công."
  2. Lưu notification qua `INotificationRepository` + `IUnitOfWork.SaveChangesAsync()`
  3. Gọi `INotificationPusher.PushAsync(SubmittedByUserId, notification)` để push SSE real-time

**Đăng ký consumer:**
- Thêm consumer vào MassTransit config trong NotificationService.Infrastructure

**Files cần tạo/sửa:**
1. **[Mới]** `src/BuildingBlocks/Contracts/IntegrationEvents/FormSubmittedIntegrationEvent.cs`
2. **[Mới]** `src/Services/NotificationService/NotificationService.Application/EventHandlers/FormSubmittedIntegrationEventHandler.cs`
3. **[Sửa]** `src/Services/DynamicFormService/DynamicFormService.Application/Features/SubmitForm/SubmitFormCommandHandler.cs` — sau SaveChanges thêm publish event qua IEventBus
4. **[Sửa]** `src/Services/NotificationService/NotificationService.Infrastructure/` — đăng ký FormSubmittedIntegrationEventHandler với MassTransit

**Yêu cầu:**
- Contract phải nằm trong BuildingBlocks/Contracts, không được đặt trong service.
- Hỏi tôi trước khi sửa hơn 3 file cùng lúc.
- Không sửa logic hiện có ngoài phạm vi task này.
```

---

## Template 5 — Viết Unit Test

### Template

```
Viết unit test cho **[TÊN_CLASS_CẦN_TEST]** trong **[TÊN_SERVICE]**.

**File test đặt tại:**
`tests/[TÊN_SERVICE].Tests/[LAYER]/[TÊN_FEATURE]/[TÊN_CLASS_CẦN_TEST]Tests.cs`

**Class cần test:** `[TÊN_CLASS_CẦN_TEST]`
(nằm tại `src/Services/[TÊN_SERVICE]/[TÊN_SERVICE].[LAYER]/[PATH]`)

**Dependencies cần mock (dùng NSubstitute):**
- `[INTERFACE_1]` — VD: `IUserRepository`, `IOrderRepository`
- `[INTERFACE_2]` — VD: `IUnitOfWork`, `IEventBus`
- `[INTERFACE_3]` — VD: `IJwtTokenIssuer`, `IPasswordHasher<User>`

**Các test case cần cover:**

| Scenario | Input | Expected Result |
|----------|-------|----------------|
| [HAPPY_PATH] | [INPUT_HỢP_LỆ] | `Result.IsSuccess == true`, value là `[EXPECTED_VALUE]` |
| [ERROR_CASE_1] | [INPUT_SAI — VD: email không tồn tại] | `Result.IsFailure == true`, `Error.Code == "[ERROR_CODE]"` |
| [ERROR_CASE_2] | [INPUT_SAI — VD: password sai] | `Result.IsFailure == true`, `Error.Code == "[ERROR_CODE]"` |
| [SIDE_EFFECT_TEST] | [INPUT_HỢP_LỆ] | [DEPENDENCY].Received(1).[METHOD_CALL] |

**Convention:**
- Framework: xUnit (method `[Fact]`, `[Theory]`)
- Mock: NSubstitute (`Substitute.For<IInterface>()`)
- Assertions: FluentAssertions (`.Should().BeTrue()`, `.Should().Be(...)`)
- Test method name pattern: `[MethodName]_[Scenario]_[ExpectedOutcome]`
  Ví dụ: `Handle_WithValidCredentials_ShouldReturnToken`

**Yêu cầu:**
- Không tạo test kết nối DB thật hay RabbitMQ thật
- Chỉ tạo file trong thư mục `tests/`
- Không sửa production code
```

---

### Ví dụ điền sẵn — Test LoginUserCommandHandler

```
Viết unit test cho **LoginUserCommandHandler** trong **AuthService**.

**File test đặt tại:**
`tests/AuthService.Tests/Application/Login/LoginUserCommandHandlerTests.cs`

**Class cần test:** `LoginUserCommandHandler`
(nằm tại `src/Services/AuthService/AuthService.Application/Features/Login/LoginUserCommandHandler.cs`)

**Dependencies cần mock (dùng NSubstitute):**
- `IUserRepository`
- `IPasswordHasher<User>`
- `IJwtTokenIssuer`
- `IUnitOfWork`

**Các test case cần cover:**

| Scenario | Input | Expected Result |
|----------|-------|----------------|
| Đăng nhập thành công | Email/password hợp lệ | `IsSuccess == true`, `Value.Token` không rỗng |
| Email không tồn tại | Email không có trong DB | `IsFailure == true`, `Error.Code == "User.NotFound"` |
| Sai mật khẩu | Password không match hash | `IsFailure == true`, `Error.Code == "Auth.WrongPassword"` |
| Token được issue | Email/password hợp lệ | `IJwtTokenIssuer` nhận được call `Received(1).Issue(...)` |

**Convention:**
- Framework: xUnit
- Mock: NSubstitute
- Assertions: FluentAssertions
- Test method name pattern: `Handle_[Scenario]_[ExpectedOutcome]`

**Yêu cầu:**
- Không tạo test kết nối DB thật hay RabbitMQ thật
- Chỉ tạo file trong thư mục `tests/`
- Không sửa production code
```

---

## Template 6 — Refactor (Không Thay Đổi Behavior)

### Template

```
Refactor **[MÔ_TẢ_PHẠM_VI]** trong **[TÊN_SERVICE]**.

**File(s) cần refactor:**
- `src/Services/[TÊN_SERVICE]/[TÊN_SERVICE].[LAYER]/[PATH_FILE_1]`
- `src/Services/[TÊN_SERVICE]/[TÊN_SERVICE].[LAYER]/[PATH_FILE_2]` (nếu có)

**Lý do refactor:**
[MÔ_TẢ_VẤN_ĐỀ_HIỆN_TẠI — VD: code trùng lặp, không theo pattern chuẩn, vi phạm nguyên tắc nào]

**Mục tiêu sau refactor:**
- [MỤC_TIÊU_1 — VD: Dùng base class `Repository<T, TId>` thay vì viết CRUD thủ công]
- [MỤC_TIÊU_2 — VD: Tách handler thành các private method nhỏ hơn]

**Ràng buộc bắt buộc:**
- KHÔNG thay đổi public API (tên method, signature, return type)
- KHÔNG thay đổi behavior (cùng input → cùng output)
- KHÔNG sửa file nào ngoài danh sách trên
- KHÔNG thêm feature mới, KHÔNG xóa logic hiện có (trừ khi là code chết rõ ràng)

**Definition of done:**
- [ ] Tất cả test hiện có vẫn pass sau refactor
- [ ] Không có breaking change
- [ ] [TIÊU_CHÍ_BỔ_SUNG nếu có]

**Yêu cầu:**
- Hỏi tôi trước khi sửa hơn 3 file.
- Nếu phát hiện bug trong khi refactor, báo cáo nhưng KHÔNG tự sửa — tạo một task riêng.
```

---

### Ví dụ điền sẵn — Refactor M01Service repository dùng base Repository

```
Refactor **M01Repository** trong **M01Service** để kế thừa `Repository<T, TId>` từ SharedKernel thay vì tự implement CRUD thủ công.

**File(s) cần refactor:**
- `src/Services/M01Service/M01Service.Infrastructure/Persistence/Repositories/M01Repository.cs`

**Lý do refactor:**
M01Repository hiện đang tự viết lại các method `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` — những method này đã có sẵn trong `Repository<T, TId>` tại `src/BuildingBlocks/Common/Persistence/Repository.cs`. Code bị trùng lặp và không nhất quán với các service khác (AuthService, OrderService đều đã dùng base class này).

**Mục tiêu sau refactor:**
- `M01Repository` kế thừa `Repository<M01Entity, Guid>` (hoặc entity type phù hợp)
- Chỉ giữ lại các method đặc thù của M01 không có trong base class
- Behavior giống hệt trước khi refactor

**Ràng buộc bắt buộc:**
- KHÔNG thay đổi public API của `IM01Repository` interface
- KHÔNG thay đổi behavior (cùng input → cùng output)
- KHÔNG sửa file nào ngoài `M01Repository.cs`
- KHÔNG thêm feature mới, KHÔNG xóa method đặc thù của M01

**Definition of done:**
- [ ] Tất cả test hiện có trong `tests/M01Service.Tests/` vẫn pass sau refactor
- [ ] Không có breaking change với `IM01Repository` interface
- [ ] File ngắn hơn file gốc (bỏ được code trùng lặp)

**Yêu cầu:**
- Hỏi tôi trước khi sửa hơn 3 file.
- Nếu phát hiện bug trong khi refactor, báo cáo nhưng KHÔNG tự sửa.
```

---

## Template 7 — Review Code Sau Generate (Vibe & Verify Bước 4)

### Template

```
Hãy review code vừa được generate cho task: **[MÔ_TẢ_NGẮN_TASK]**

**Spec đã approve:**
[DÁN_SPEC_TỪ_BƯỚC_2_VÀO_ĐÂY]

**Files đã generate:**
- [DANH_SÁCH_FILE_ĐÃ_TẠO/SỬA]

**Yêu cầu review — kiểm tra theo thứ tự:**

1. **Match Spec**: Từng business rule trong spec có được implement đúng không?
   Liệt kê từng rule và kết luận: ✓ Đúng / ✗ Sai / ⚠ Thiếu

2. **Convention (CLAUDE.md)**:
   - Handler có trả Result<T> không, hay đang throw exception?
   - Entity có được tạo qua static factory Create() không?
   - Có public setter nào trên Entity không?
   - Có business logic nào trong Controller/Repository không?
   - Validator có đủ rule như spec không?

3. **Scope**: Có file nào bị sửa ngoài danh sách đã approve không?

**Output mong muốn:**
Một danh sách issues theo format:
Issue #N: [mô tả]
File: [đường dẫn]
Severity: [blocking | warning]
Fix: [gợi ý sửa cụ thể]

Nếu không có issue: "Review passed. Sẵn sàng Ship."
```

---

### Ví dụ điền sẵn — Review GetFormSubmissionsQuery

```
Hãy review code vừa được generate cho task: **GetFormSubmissionsQuery trong DynamicFormService**

**Spec đã approve:**
API Contract: GET /dynamic-forms/submissions?userId={userId}
Business Rules:
- Rule 1: Chỉ trả submission của đúng userId được request
- Rule 2: Trả 404 nếu userId không tồn tại
- Rule 3: Trả danh sách rỗng nếu user không có submission nào (không phải lỗi)

**Files đã generate:**
- DynamicFormService.Application/Features/GetFormSubmissions/GetFormSubmissionsQuery.cs
- DynamicFormService.Application/Features/GetFormSubmissions/GetFormSubmissionsQueryHandler.cs
- DynamicFormService.Application/Features/GetFormSubmissions/GetFormSubmissionsQueryValidator.cs

**Yêu cầu review — kiểm tra theo thứ tự:**

1. **Match Spec**: Rule 1, 2, 3 có implement đúng không?
2. **Convention**: Result<T>, factory method, không có public setter, validator đủ rule
3. **Scope**: Chỉ 3 file trên, không được sửa gì khác

**Output mong muốn:**
Danh sách issues theo format Issue #N hoặc "Review passed."
```

---

## Template 8 — Fix Issue Cụ Thể (Vibe & Verify Bước 5)

### Template

```
Fix issue sau trong code vừa generate. Chỉ fix đúng issue này, không sửa gì khác.

**Issue:**
[MÔ_TẢ_ISSUE — VD: "Handler đang throw NotFoundException thay vì trả Result.Failure(Error.NotFound(...))"]

**File cần sửa:**
`[ĐƯỜNG_DẪN_FILE]` — dòng [SỐ_DÒNG nếu biết]

**Spec gốc (làm căn cứ):**
[ĐOẠN_SPEC_LIÊN_QUAN — chỉ phần liên quan đến issue này]

**Fix yêu cầu:**
[MÔ_TẢ_FIX — VD: "Thay throw bằng return Result.Failure(Error.NotFound('FormSubmission.NotFound', 'Không tìm thấy submission'))"]

**Ràng buộc:**
- Chỉ sửa đúng file và vấn đề được mô tả
- Không refactor code xung quanh
- Không sửa file khác dù thấy vấn đề — báo cáo, không tự sửa
```

---

### Ví dụ điền sẵn — Fix handler đang throw exception

```
Fix issue sau trong code vừa generate. Chỉ fix đúng issue này, không sửa gì khác.

**Issue:**
Handler đang dùng `throw new NotFoundException(...)` thay vì trả `Result.Failure(Error.NotFound(...))`

**File cần sửa:**
`src/Services/DynamicFormService/DynamicFormService.Application/Features/GetFormSubmissions/GetFormSubmissionsQueryHandler.cs`

**Spec gốc:**
Business Rule 2: Trả 404 nếu userId không tồn tại
Convention CLAUDE.md: Handler KHÔNG ném exception — dùng Result.Failure(Error.NotFound("User.NotFound", "..."))

**Fix yêu cầu:**
Thay đoạn throw bằng:
return Result.Failure<IReadOnlyList<FormSubmissionDto>>(Error.NotFound("User.NotFound", "User không tồn tại"));

**Ràng buộc:**
- Chỉ sửa GetFormSubmissionsQueryHandler.cs
- Không refactor code xung quanh
- Nếu thấy issue khác trong file → báo cáo, không tự sửa
```

---

## Quick Reference — Placeholder Lookup

| Placeholder | Giá trị có thể dùng |
|-------------|---------------------|
| `[TÊN_SERVICE]` | `AuthService`, `OrderService`, `NotificationService`, `M01Service`, `DataMatchingService`, `DynamicFormService`, `AsyncGateway` |
| `[LAYER]` | `Domain`, `Application`, `Infrastructure`, `API` |
| `[PERMISSION_STRING]` | `"orders:create"`, `"orders:read"`, `"orders:update"`, `"orders:delete"`, `"users:manage"`, `"roles:manage"`, `"notifications:read"`, `"notifications:send"`, `"m01:read"`, `"m01:write"`, `"async:submit"` |
| `[TÊN_INTEGRATION_EVENT]` | Existing: `OrderCreateRequestedIntegrationEvent`, `OrderCreatedIntegrationEvent`, `OrderConfirmedIntegrationEvent`, `UserRegisteredIntegrationEvent`, `UserLoggedInIntegrationEvent`, `NotificationSendRequestedIntegrationEvent`, `FormSubmittedIntegrationEvent`, `BaoCaoKhoaCreatedIntegrationEvent`, `DashboardFeReadyIntegrationEvent` |
| `[RETURN_TYPE]` | `Guid` (ID của entity mới), `XxxDto`, `IReadOnlyList<XxxDto>`, void → dùng `Result` không có generic |
| `[DATABASE]` | SQL Server: Auth, Order, Notification, M01 · PostgreSQL: DataMatching, DynamicForm |
