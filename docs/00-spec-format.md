# 00 — Hướng dẫn viết Specification cho AI

> **Mục đích của doc này:** Định nghĩa một format chuẩn để viết spec kỹ thuật sao cho AI (LLM) có thể đọc và implement chính xác mà không cần hỏi thêm. Dùng doc này như template mỗi khi thiết kế feature mới.

---

## Tại sao cần format riêng cho AI?

AI implement tốt khi spec có:

| Yêu cầu | Ví dụ tốt | Ví dụ xấu |
|---------|-----------|-----------|
| **Kiểu dữ liệu rõ ràng** | `string (max 50, regex ^[a-z0-9\-]+$)` | "tên ngắn gọn" |
| **Enum liệt kê đầy đủ** | `0=Draft, 1=Published, 2=Archived` | "các trạng thái" |
| **Transition state rõ** | `Draft→Published: khi có ≥1 field` | "publish khi đủ điều kiện" |
| **Side effect tường minh** | `Emits FormSubmittedIntegrationEvent` | "gửi notification" |
| **Error case cụ thể** | `404 nếu formKey không tồn tại hoặc chưa Published` | "lỗi nếu không tìm thấy" |
| **Constraint phân biệt MUST/SHOULD** | `MUST unique per module` | "nên unique" |

---

## Quy ước ngôn ngữ

| Từ khóa | Ý nghĩa | Enforce bởi |
|---------|---------|-------------|
| **MUST** | Bắt buộc, exception nếu vi phạm | Domain/Validator |
| **MUST NOT** | Cấm tuyệt đối | Domain guard |
| **SHOULD** | Khuyến nghị, warn nếu vi phạm | Business logic |
| **MAY** | Tùy chọn | Không enforce |
| `→` | Chuyển state | State machine |
| `[R]` | Readonly sau khi set | Property setter |
| `[JSONB]` | Lưu dạng JSON trong DB | EF config |
| `[DENORM]` | Denormalized — copy từ entity khác | Lý do: tránh JOIN |

---

## Template 1 — Enum

```markdown
## Enum: `TênEnum`

> Ngữ cảnh: dùng ở đâu, serialize thành gì trong JSON/DB.

| Int | Name | Mô tả | Transition hợp lệ từ |
|-----|------|-------|----------------------|
| 0   | Draft | ... | — (trạng thái đầu) |
| 1   | Published | ... | Draft |
| 2   | Archived | ... | Published |

**Serialize:** `string` (lowercase tên) trong JSON response; `int` trong DB.
```

---

## Template 2 — Value Object (record bất biến)

```markdown
## Value Object: `TênRecord`

> Ngữ cảnh: nhúng trong entity nào, lưu dạng [JSONB] hay column riêng.

| Field | Type | Constraint | Mặc định |
|-------|------|-----------|---------|
| `FieldName` | `string` | NotEmpty, max 50 | — |
| `Count` | `int` | ≥ 0 | `0` |

**Validation:** [mô tả các rule phụ thuộc nhau nếu có]
**Serialize:** JSON object — `{ "fieldName": "...", "count": 0 }`
```

---

## Template 3 — Entity / Aggregate

```markdown
## Entity: `TênEntity`

> Aggregate root / child entity. DB table: `TênBảng`.

### Fields

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK, generated | `ValueGeneratedNever()` |
| `Code` | `string` | MUST unique global; max 50; `^[a-z0-9\-]+$` | [R] |
| `Status` | `TênEnum` | — | Default: `Draft` |
| `DataJson` | `string` | — | [JSONB] — serialized `TênRecord` |

### State Machine

```
Initial ──Create()──→ Draft ──Publish()──→ Published ──Archive()──→ Archived
```

| Transition | Trigger | Precondition | Side Effect |
|-----------|---------|--------------|-------------|
| `→ Draft` | `Create()` | — | Raise `XxxCreatedDomainEvent` |
| `→ Published` | `Publish()` | MUST có ≥ 1 field | Raise `XxxPublishedDomainEvent`; increment `Version` |
| `→ Archived` | `Archive()` | — | — |

### Business Rules

1. **[RULE-01]** Code MUST unique trong phạm vi [global / module / form].
2. **[RULE-02]** Published entity MUST NOT được sửa field — guard trong domain method.
3. **[RULE-03]** Archived entity MUST NOT chuyển về Draft hay Published.

### Methods

```csharp
// Factory — dùng thay constructor
static TênEntity Create(params...) → new entity ở trạng thái Draft

// Mutation
void Publish()      // MUST: ≥1 field; raises PublishedDomainEvent
void Archive()      // MUST NOT: từ Archived
void Update(...)    // MUST NOT: nếu Published
```
```

---

## Template 4 — API Endpoint

```markdown
## Endpoint: `[METHOD] /path/{param}`

> Mô tả một dòng về mục đích.  
> Auth: `[AllowAnonymous]` / `[Authorize(Roles="admin")]` / `[Authorize(Policy="xxx")]`

### Request

**Route params:** `param` — Guid, required

**Body:**
```json
{
  "fieldA": "string (NotEmpty, max 100)",
  "fieldB": 0,
  "fieldC": ["string"],
  "fieldD": null
}
```

### Validation

| Field | Rule | Error |
|-------|------|-------|
| `fieldA` | NotEmpty, max 100, `^[a-z]+$` | 400 |
| `fieldB` | ≥ 0 | 400 |

### Response

| Code | Khi nào | Body |
|------|---------|------|
| 200 | Thành công | `ApiResponse<TênDto>` |
| 400 | Validation fail | `ApiResponse.Fail(code, message)` |
| 404 | Entity không tồn tại | `ApiResponse.Fail(...)` |
| 409 | Conflict (duplicate key) | `ApiResponse.Fail(...)` |

### Side Effects

- **Emits:** `TênIntegrationEvent { Field1, Field2 }` qua MassTransit outbox
- **Mutates:** Entity A → trạng thái B
- **Revokes:** Entity C nếu điều kiện X
```

---

## Template 5 — Integration Event

```markdown
## Event: `TênIntegrationEvent`

> Publish khi nào, consumer nào lắng nghe.

| Field | Type | Mô tả |
|-------|------|-------|
| `EntityId` | `Guid` | ID của entity gây ra event |
| `Code` | `string` | Identifier |
| `OccurredAt` | `DateTime` | UTC timestamp |

**Publisher:** `TênService` — qua `IEventBus.PublishAsync()`  
**Consumer(s):** `TênService` — để làm gì
```

---

## Checklist trước khi viết spec

- [ ] Tất cả enum có int value và mô tả rõ ràng
- [ ] Tất cả string field có max length và regex nếu cần
- [ ] State machine vẽ ra, không chỉ mô tả văn xuôi
- [ ] Mỗi transition có precondition và side effect
- [ ] Mỗi endpoint có đủ: auth, request schema, validation table, response codes, side effects
- [ ] Business rules đánh số `[RULE-NN]` để tham chiếu chéo
- [ ] Phân biệt MUST (enforce bằng code) vs SHOULD (không enforce)
- [ ] JSONB field ghi rõ schema của JSON bên trong

---

## Ví dụ thực tế

Xem **[32 — DynamicFormService Technical Spec](./32-dynamic-form-spec.md)** — toàn bộ spec của DynamicFormService được viết theo format này.
