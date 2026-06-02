# 32 — DynamicFormService: Technical Specification

> **Loại doc:** Technical Spec — viết theo format [00-spec-format.md](./00-spec-format.md).  
> **Dùng để:** AI implement feature mới, onboard dev, review PR.  
> Xem architecture overview tại [29](./29-dynamic-form-service.md), API walkthrough tại [30](./30-dynamic-form-service-api.md).

---

## Mục lục

1. [Enums](#1-enums)
2. [Value Objects](#2-value-objects)
3. [Entities](#3-entities)
4. [Repositories](#4-repositories)
5. [API Endpoints](#5-api-endpoints)
6. [Integration Events](#6-integration-events)
7. [Business Rules tổng hợp](#7-business-rules-tổng-hợp)
8. [Validation Rules tổng hợp](#8-validation-rules-tổng-hợp)

---

## 1. Enums

### Enum: `FormStatus`

> Trạng thái vòng đời của `FormTemplate`. Serialize thành `string` (lowercase) trong JSON response; `string` trong DB (`HasConversion<string>()`).

| Int | Name | Mô tả | Transition hợp lệ từ |
|-----|------|-------|----------------------|
| 0 | `Draft` | Đang soạn thảo, chỉ admin thấy, có thể sửa field | — (trạng thái đầu) |
| 1 | `Published` | Public dùng được, MUST NOT sửa field | `Draft` |
| 2 | `Archived` | Không nhận submission mới, readonly | `Published` |

**Serialize JSON:** `"draft"` / `"published"` / `"archived"`

---

### Enum: `FormPageStatus`

> Trạng thái vòng đời của `FormPage`. Cùng semantic với `FormStatus`.

| Int | Name | Mô tả | Transition hợp lệ từ |
|-----|------|-------|----------------------|
| 0 | `Draft` | Đang soạn, chỉ admin thấy | — |
| 1 | `Published` | Public có thể render | `Draft` |
| 2 | `Archived` | Không cập nhật được | `Published` |

---

### Enum: `ModuleStatus`

> Trạng thái hoạt động của `FormModule`.

| Int | Name | Mô tả |
|-----|------|-------|
| 0 | `Active` | Đang hoạt động — query `GetAllActive` trả về |
| 1 | `Inactive` | Tạm dừng — không xuất hiện trong list public |

**Lưu ý:** Module inactive không block submission — form vẫn nhận submit nếu đã Published.

---

### Enum: `SubmissionStatus`

> Trạng thái xử lý của `FormSubmission`.

| Int | Name | Mô tả | Transition từ |
|-----|------|-------|--------------|
| 0 | `Submitted` | Vừa được gửi, chưa review | — (mặc định khi tạo) |
| 1 | `Reviewed` | Admin đã xem | `Submitted` |

---

### Enum: `FieldType`

> Loại input của `FormField`. Quyết định cách frontend render và validate phía client.

| Int | Name | Mô tả | Cần `Options`? | Ghi chú |
|-----|------|-------|----------------|---------|
| 0 | `Text` | Input text một dòng | Không | — |
| 1 | `Textarea` | Input text nhiều dòng | Không | — |
| 2 | `Number` | Input số | Không | Client validate `min`/`max` từ `ValidationRules` |
| 3 | `Date` | Chọn ngày (date picker) | Không | Format ISO 8601 date |
| 4 | `DateTime` | Chọn ngày giờ | Không | Format ISO 8601 datetime |
| 5 | `Select` | Dropdown chọn một | **SHOULD** | Options là danh sách `{label, value}` |
| 6 | `MultiSelect` | Dropdown chọn nhiều | **SHOULD** | Trả về mảng string |
| 7 | `Radio` | Radio buttons | **SHOULD** | Chọn một trong options |
| 8 | `Checkbox` | Single checkbox | Không | Trả về `"true"` / `"false"` |
| 9 | `File` | Upload file | Không | Frontend tự handle upload |
| 10 | `Signature` | Ký tên (canvas) | Không | Trả về base64 PNG |
| 11 | `Section` | Tiêu đề phân cách, không nhập liệu | Không | Dùng để group field |

**Serialize JSON:** `"text"` / `"select"` / ... (lowercase tên enum)

---

### Enum: `FieldWidth`

> Độ rộng field trong layout grid 12 cột.

| Int | Name | Số cột | CSS tương đương |
|-----|------|--------|----------------|
| 0 | `Full` | 12/12 | `col-span-12` |
| 1 | `Half` | 6/12 | `col-span-6` |
| 2 | `Third` | 4/12 | `col-span-4` |

**Serialize JSON:** `"full"` / `"half"` / `"third"`

---

## 2. Value Objects

### Value Object: `FieldOption`

> Một lựa chọn trong field `Select`, `MultiSelect`, `Radio`. Lưu dạng `[JSONB]` trong `FormField.OptionsJson` (mảng).

| Field | Type | Constraint |
|-------|------|-----------|
| `Label` | `string` | MUST NotEmpty — text hiển thị cho user |
| `Value` | `string` | MUST NotEmpty — giá trị lưu vào submission |

**Serialize:** `[{"label": "Nam", "value": "male"}, {"label": "Nữ", "value": "female"}]`

---

### Value Object: `ValidationRule`

> Một rule validate client-side cho field. Lưu dạng `[JSONB]` trong `FormField.ValidationRulesJson` (mảng).

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Type` | `string` | MUST một trong: `required`, `minLength`, `maxLength`, `pattern`, `min`, `max` | Xem bảng Type bên dưới |
| `Value` | `string` | MUST NotEmpty | Giá trị constraint |
| `ErrorMessage` | `string` | MUST NotEmpty | Thông báo lỗi tùy chỉnh hiển thị cho user |

**Bảng `Type`:**

| Type | Áp dụng cho FieldType | `Value` là | Ví dụ |
|------|----------------------|-----------|-------|
| `required` | Tất cả | `"true"` | Field bắt buộc |
| `minLength` | `Text`, `Textarea` | số nguyên dương | Tối thiểu N ký tự |
| `maxLength` | `Text`, `Textarea` | số nguyên dương | Tối đa N ký tự |
| `pattern` | `Text` | regex string | Validate format |
| `min` | `Number`, `Date`, `DateTime` | số / ISO date | Giá trị tối thiểu |
| `max` | `Number`, `Date`, `DateTime` | số / ISO date | Giá trị tối đa |

**Serialize:** `[{"type": "required", "value": "true", "errorMessage": "Bắt buộc nhập"}]`

---

### Value Object: `ConditionalLogic`

> Logic hiển thị/ẩn field dựa trên giá trị field khác. Lưu dạng `[JSONB]` trong `FormField.ConditionalLogicJson` (single object, không phải mảng).

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `SourceFieldKey` | `string` | MUST NotEmpty; MUST là key của field khác trong cùng form | Field kích hoạt điều kiện |
| `Operator` | `string` | MUST một trong: `"Equals"`, `"NotEquals"`, `"Contains"` | Phép so sánh |
| `Value` | `string` | MUST NotEmpty | Giá trị so sánh |
| `Action` | `string` | MUST một trong: `"Show"`, `"Hide"` | Hành động khi điều kiện đúng |

**Ví dụ:** Hiện field `diabetes_type` khi field `has_diabetes` = `"true"`:
```json
{
  "sourceFieldKey": "has_diabetes",
  "operator": "Equals",
  "value": "true",
  "action": "Show"
}
```

---

### Value Object: `FormSettings`

> Cấu hình hiển thị của form. Lưu dạng `[JSONB]` trong `FormTemplate.SettingsJson`.

| Field | Type | Constraint | Mặc định |
|-------|------|-----------|---------|
| `SubmitButtonLabel` | `string` | MUST NotEmpty, max 100 | `"Gửi"` |
| `SuccessMessage` | `string` | MUST NotEmpty, max 500 | `"Đã gửi form thành công"` |
| `AllowMultipleSubmissions` | `bool` | — | `true` |

---

### Value Object: `FieldAnswer`

> Câu trả lời của user cho một field. Lưu dạng `[JSONB]` trong `FormSubmission.AnswersJson` (mảng).

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `FieldKey` | `string` | MUST NotEmpty | Key của field trong form |
| `Value` | `string?` | MAY null | `null` nếu bỏ qua field; mảng serialize thành JSON string |

---

### Value Object: `FormPageLayout`

> Layout của `FormPage`. Lưu dạng `[JSONB]` trong `FormPage.LayoutJson`. Default khi tạo: `{"rows":[]}`.

| Field | Type | Constraint |
|-------|------|-----------|
| `Rows` | `List<FormPageRow>` | MUST NotNull; MAY empty |

**FormPageRow:**

| Field | Type | Constraint |
|-------|------|-----------|
| `Components` | `List<FormPageComponent>` | MUST NotNull; MAY empty |

**FormPageComponent** — polymorphic, phân biệt bằng discriminator field `"type"`:

| `"type"` | C# Type | Fields | Mô tả |
|----------|---------|--------|-------|
| `"FormSection"` | `FormSectionPageComponent` | `Span?: int`, `FormKey: string`, `Title?: string` | Nhúng form vào trang; `FormKey` MUST là key form đã Published |
| `"TextBlock"` | `TextBlockPageComponent` | `Span?: int`, `Content: string`, `Align?: string` | Khối văn bản; `Align` ∈ `"left"`, `"center"`, `"right"` |
| `"Divider"` | `DividerPageComponent` | `Span?: int`, `Label?: string` | Đường phân cách |

`Span` là số cột (1–12) trong grid. `null` = full width.

---

## 3. Entities

### Entity: `FormModule`

> Aggregate root. DB table: `FormModules`. Nhóm các form liên quan theo nghiệp vụ.

**Fields:**

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK, `ValueGeneratedNever()` | — |
| `Code` | `string` | MUST unique global; max 50; `^[a-z0-9\-]+$` | [R] sau khi tạo |
| `Name` | `string` | MUST NotEmpty; max 200 | — |
| `Description` | `string?` | max 500; MAY null | — |
| `Status` | `ModuleStatus` | — | Default: `Active` |
| `CreatedAtUtc` | `DateTime` | — | Set khi `Create()` |
| `UpdatedAtUtc` | `DateTime?` | — | Set khi `Update()`, `Activate()`, `Deactivate()` |

**State Machine:**

```
──Create()──→ Active ──Deactivate()──→ Inactive ──Activate()──→ Active
```

| Method | Precondition | Side Effect |
|--------|-------------|------------|
| `Create(code, name, desc)` | Code MUST unique | Raise `FormModuleCreatedDomainEvent` |
| `Update(name, desc)` | — | Set `UpdatedAtUtc` |
| `Deactivate()` | — | `Status = Inactive` |
| `Activate()` | — | `Status = Active` |

---

### Entity: `FormTemplate`

> Aggregate root. DB table: `FormTemplates`. Chứa danh sách `FormField` (child entities).

**Fields:**

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK | — |
| `ModuleId` | `Guid` | FK → `FormModules.Id` | — |
| `ModuleCode` | `string` | max 50 | [DENORM] copy từ Module.Code — tránh JOIN |
| `Key` | `string` | MUST unique trong module; max 100; `^[a-z0-9\-]+$` | [R] |
| `Name` | `string` | MUST NotEmpty; max 200 | — |
| `Description` | `string?` | max 500 | — |
| `Status` | `FormStatus` | — | Default: `Draft` |
| `Version` | `int` | ≥ 1 | Default: `1`; tăng khi `Publish()` |
| `SettingsJson` | `string` | [JSONB] — `FormSettings` | — |
| `Fields` | `IReadOnlyCollection<FormField>` | — | Navigation property |

**State Machine:**

```
──Create()──→ Draft ──Publish()──→ Published ──Archive()──→ Archived
```

| Method | Precondition | Side Effect |
|--------|-------------|------------|
| `Create(...)` | ModuleId MUST tồn tại; Key MUST unique trong module | — |
| `AddField(...)` | MUST NOT `Published` hoặc `Archived`; FieldKey MUST unique trong form | — |
| `Publish()` | MUST có ≥ 1 field; MUST `Draft` | Raise `FormPublishedDomainEvent`; `Version++` |
| `Archive()` | MUST `Published` | `Status = Archived` |
| `Update(name, desc, settings)` | MUST NOT `Published` hoặc `Archived` | — |

---

### Entity: `FormField`

> Child entity của `FormTemplate`. DB table: `FormFields`.

**Fields:**

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK | — |
| `FormTemplateId` | `Guid` | FK → `FormTemplates.Id` | — |
| `Key` | `string` | MUST unique trong form; max 100; `^[a-z0-9_]+$` | Dùng `_` thay `-` khác với các key khác |
| `Label` | `string` | MUST NotEmpty; max 200 | Hiển thị cho user |
| `FieldType` | `FieldType` | — | Xem Enum bên trên |
| `Order` | `int` | ≥ 0 | Thứ tự render, nhỏ hơn render trước |
| `Required` | `bool` | — | Server không validate — client dùng `ValidationRules` |
| `Width` | `FieldWidth` | — | Default: `Full` |
| `Placeholder` | `string?` | max 300 | — |
| `HelpText` | `string?` | max 500 | Hướng dẫn nhập |
| `OptionsJson` | `string?` | [JSONB] — `List<FieldOption>` | SHOULD có nếu `FieldType` ∈ `Select`, `MultiSelect`, `Radio` |
| `ValidationRulesJson` | `string?` | [JSONB] — `List<ValidationRule>` | — |
| `ConditionalLogicJson` | `string?` | [JSONB] — `ConditionalLogic` | Single object, không phải array |

**Method:**

| Method | Precondition |
|--------|-------------|
| `Update(label, order, required, width, ...)` | Gọi từ form MUST NOT `Published` (guard ở `FormTemplate.AddField`) |

---

### Entity: `FormSubmission`

> Aggregate root. DB table: `FormSubmissions`. Immutable sau khi tạo — chỉ `Status` thay đổi.

**Fields:**

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK | — |
| `FormTemplateId` | `Guid` | FK → `FormTemplates.Id` | — |
| `ModuleCode` | `string` | max 50 | [DENORM] |
| `FormKey` | `string` | max 100 | [DENORM] |
| `FormVersion` | `int` | — | Capture version tại thời điểm submit — form có thể publish lại sau |
| `SubmittedBy` | `Guid?` | MAY null | null nếu anonymous |
| `Status` | `SubmissionStatus` | — | Default: `Submitted` |
| `AnswersJson` | `string` | [JSONB] — `List<FieldAnswer>` | — |
| `SubmittedAt` | `DateTime` | — | UTC, set khi `Create()` |

**Method:**

| Method | Precondition | Side Effect |
|--------|-------------|------------|
| `Create(...)` | Form MUST `Published` (check trước khi gọi) | Raise `FormSubmittedDomainEvent` |
| `MarkReviewed()` | MUST `Submitted` | `Status = Reviewed` |

---

### Entity: `FormPage`

> Aggregate root. DB table: `FormPages`. Kết hợp nhiều form vào một màn hình layout.

**Fields:**

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK | — |
| `ModuleId` | `Guid` | FK → `FormModules.Id` | — |
| `ModuleCode` | `string` | max 50 | [DENORM] |
| `Code` | `string` | MUST unique trong module; max 100; `^[a-z0-9\-]+$` | [R] |
| `Title` | `string` | MUST NotEmpty; max 200 | — |
| `Description` | `string?` | max 500 | — |
| `Status` | `FormPageStatus` | — | Default: `Draft` |
| `LayoutJson` | `string` | [JSONB] — `FormPageLayout` | Default: `{"rows":[]}` |

**State Machine:**

```
──Create()──→ Draft ──Publish()──→ Published ──Archive()──→ Archived
```

| Method | Precondition | Side Effect |
|--------|-------------|------------|
| `Create(...)` | Code MUST unique trong module | — |
| `UpdateLayout(layoutJson)` | MUST NOT `Archived` | — |
| `Publish()` | MUST NOT `Archived` | Raise `FormPagePublishedDomainEvent` |
| `Archive()` | — | `Status = Archived` |

---

## 4. Repositories

### `IFormModuleRepository`

| Method | Trả về | Ghi chú |
|--------|--------|---------|
| `GetByIdAsync(id, ct)` | `FormModule?` | — |
| `GetByCodeAsync(code, ct)` | `FormModule?` | — |
| `GetAllActiveAsync(ct)` | `List<FormModule>` | Filter `Status == Active`, order by `Name` |
| `ExistsByCodeAsync(code, ct)` | `bool` | Dùng để check duplicate trước `Create` |
| `AddAsync(module, ct)` | `void` | Chưa commit |

### `IFormTemplateRepository`

| Method | Trả về | Ghi chú |
|--------|--------|---------|
| `GetByIdAsync(id, includeFields, ct)` | `FormTemplate?` | `includeFields=true` → eager load `Fields` |
| `GetByModuleAndKeyAsync(moduleCode, formKey, includeFields, ct)` | `FormTemplate?` | Dùng khi cần schema by slug |
| `GetByModuleCodeAsync(moduleCode, ct)` | `List<FormTemplate>` | Không include fields |
| `ExistsByKeyInModuleAsync(moduleId, key, ct)` | `bool` | Validate unique trong module |
| `AddAsync(template, ct)` | `void` | Chưa commit |

### `IFormSubmissionRepository`

| Method | Trả về | Ghi chú |
|--------|--------|---------|
| `GetByFormTemplateAsync(formTemplateId, page, pageSize, ct)` | `List<FormSubmission>` | Ordered by `SubmittedAt DESC`; Skip/Take |
| `CountByFormTemplateAsync(formTemplateId, ct)` | `int` | Tổng số submission cho pagination |
| `AddAsync(submission, ct)` | `void` | Chưa commit |

### `IFormPageRepository`

| Method | Trả về | Ghi chú |
|--------|--------|---------|
| `GetByIdAsync(id, ct)` | `FormPage?` | — |
| `GetByCodeAsync(moduleCode, pageCode, ct)` | `FormPage?` | Key = `(moduleCode, pageCode)` |
| `GetByModuleAsync(moduleCode, ct)` | `List<FormPage>` | — |
| `ExistsByCodeAsync(moduleCode, pageCode, ct)` | `bool` | — |
| `Add(page)` | `void` | Sync, chưa commit |

---

## 5. API Endpoints

### `GET /forms/modules`

> Lấy danh sách tất cả module đang Active.  
> Auth: `[AllowAnonymous]`

**Response 200:**
```json
{
  "success": true,
  "data": [
    {
      "id": "guid",
      "code": "tiep-nhan",
      "name": "Tiếp nhận",
      "description": "...",
      "status": "active",
      "formCount": 3,
      "createdAtUtc": "2026-01-01T00:00:00Z"
    }
  ]
}
```

---

### `POST /forms/admin/modules`

> Tạo module mới.  
> Auth: `[Authorize(Roles="admin")]`

**Body:**
```json
{
  "code": "tiep-nhan",
  "name": "Tiếp nhận bệnh nhân",
  "description": "Nhóm form tiếp nhận"
}
```

**Validation:**

| Field | Rule |
|-------|------|
| `code` | NotEmpty, max 50, `^[a-z0-9\-]+$`, MUST unique global |
| `name` | NotEmpty, max 200 |
| `description` | max 500 |

| Code | Khi nào |
|------|---------|
| 201 | Tạo thành công |
| 409 | `code` đã tồn tại |

---

### `POST /forms/admin/modules/{moduleCode}/forms`

> Tạo form mới trong module.  
> Auth: `[Authorize(Roles="admin")]`

**Body:**
```json
{
  "key": "phieu-tiep-nhan",
  "name": "Phiếu tiếp nhận",
  "description": "Form tiếp nhận bệnh nhân",
  "submitButtonLabel": "Gửi phiếu",
  "successMessage": "Đã gửi thành công",
  "allowMultipleSubmissions": true
}
```

**Validation:**

| Field | Rule |
|-------|------|
| `key` | NotEmpty, max 100, `^[a-z0-9\-]+$`, MUST unique trong module |
| `name` | NotEmpty, max 200 |
| `moduleCode` (route) | MUST là module tồn tại và Active |

| Code | Khi nào |
|------|---------|
| 200 | Tạo thành công |
| 400 | Validation fail hoặc module không tồn tại |
| 409 | Key đã tồn tại trong module |

---

### `POST /forms/admin/forms/{formTemplateId}/fields`

> Thêm field vào form. MUST NOT form đã `Published`.  
> Auth: `[Authorize(Roles="admin")]`

**Body:**
```json
{
  "key": "ho_ten",
  "label": "Họ và tên",
  "fieldType": 0,
  "order": 0,
  "required": true,
  "width": 0,
  "placeholder": "Nhập họ tên đầy đủ",
  "helpText": null,
  "options": null,
  "validationRules": [
    { "type": "required", "value": "true", "errorMessage": "Bắt buộc nhập họ tên" },
    { "type": "maxLength", "value": "200", "errorMessage": "Không quá 200 ký tự" }
  ],
  "conditionalLogic": null
}
```

**Validation:**

| Field | Rule |
|-------|------|
| `key` | NotEmpty, max 100, `^[a-z0-9_]+$`, MUST unique trong form |
| `label` | NotEmpty, max 200 |
| `fieldType` | MUST là int hợp lệ trong `FieldType` (0–11) |
| `order` | ≥ 0 |
| `width` | MUST là int hợp lệ trong `FieldWidth` (0–2) |
| Form (`formTemplateId`) | MUST NOT `Published` hoặc `Archived` |

| Code | Khi nào |
|------|---------|
| 200 | Thêm thành công |
| 400 | Validation fail; form Published; field key trùng |
| 404 | Form không tồn tại |

---

### `POST /forms/admin/forms/{formTemplateId}/publish`

> Publish form. Form MUST có ≥ 1 field.  
> Auth: `[Authorize(Roles="admin")]`

**Side Effects:** Raise `FormPublishedDomainEvent`; `Version` tăng lên 1.

| Code | Khi nào |
|------|---------|
| 200 | Publish thành công |
| 400 | Form chưa có field; form đã Published hoặc Archived |
| 404 | Form không tồn tại |

---

### `POST /forms/admin/forms/{formTemplateId}/archive`

> Archive form. Form MUST đang `Published`.

| Code | Khi nào |
|------|---------|
| 200 | Archive thành công |
| 400 | Form không ở trạng thái `Published` |

---

### `GET /forms/{moduleCode}/{formKey}/schema`

> Lấy schema BDUI của form để frontend render. Chỉ trả về form `Published`.  
> Auth: `[AllowAnonymous]`

**Response 200:**
```json
{
  "id": "guid",
  "moduleCode": "tiep-nhan",
  "formKey": "phieu-tiep-nhan",
  "name": "Phiếu tiếp nhận",
  "description": null,
  "version": 1,
  "fields": [
    {
      "id": "guid",
      "key": "ho_ten",
      "label": "Họ và tên",
      "type": "text",
      "order": 0,
      "required": true,
      "width": "full",
      "placeholder": "Nhập họ tên",
      "helpText": null,
      "options": null,
      "validationRules": [
        { "type": "required", "value": "true", "errorMessage": "Bắt buộc" }
      ],
      "conditionalLogic": null
    }
  ],
  "settings": {
    "submitButtonLabel": "Gửi phiếu",
    "successMessage": "Đã gửi thành công",
    "allowMultipleSubmissions": true
  }
}
```

| Code | Khi nào |
|------|---------|
| 200 | Form tồn tại và Published |
| 404 | Form không tồn tại hoặc chưa/không còn Published |

---

### `POST /forms/{moduleCode}/{formKey}/submit`

> Submit form. Form MUST đang `Published`.  
> Auth: `[AllowAnonymous]` — `SubmittedBy` lấy từ JWT `sub` claim nếu có.

**Body:**
```json
{
  "answers": [
    { "fieldKey": "ho_ten", "value": "Nguyễn Văn A" },
    { "fieldKey": "ngay_sinh", "value": "1990-05-15" },
    { "fieldKey": "gioi_tinh", "value": "male" }
  ]
}
```

**Validation:**

| Field | Rule |
|-------|------|
| `answers` | MUST NotNull |
| `answers[].fieldKey` | MUST NotEmpty |

**Side Effects:**
- Raise `FormSubmittedDomainEvent`
- Publish `FormSubmittedIntegrationEvent` qua MassTransit outbox

| Code | Khi nào |
|------|---------|
| 200 | Submit thành công — trả về `{ "submissionId": "guid" }` |
| 400 | Form chưa Published hoặc Archived |
| 404 | Form không tồn tại |

---

### `GET /forms/admin/forms/{formTemplateId}/submissions`

> Lấy danh sách submission có phân trang.  
> Auth: `[Authorize(Roles="admin")]`

**Query params:**

| Param | Type | Default | Constraint |
|-------|------|---------|-----------|
| `page` | `int` | `1` | > 0 |
| `pageSize` | `int` | `20` | 1–100 |

**Response:** List `FormSubmissionDto` ordered by `SubmittedAt DESC`.

---

### `POST /forms/admin/modules/{moduleCode}/pages`

> Tạo page layout mới.  
> Auth: `[Authorize(Roles="admin")]`

**Body:**
```json
{
  "code": "tiep-nhan-toan-phan",
  "title": "Tiếp nhận toàn phần",
  "description": null
}
```

**Validation:** `code` MUST unique trong module, `^[a-z0-9\-]+$`, max 100.

---

### `PUT /forms/admin/pages/{pageId}/layout`

> Cập nhật layout JSON của page. MUST NOT page `Archived`.  
> Auth: `[Authorize(Roles="admin")]`

**Body — ví dụ layout 2 form cạnh nhau:**
```json
{
  "rows": [
    {
      "components": [
        {
          "type": "FormSection",
          "span": 6,
          "formKey": "phieu-tiep-nhan",
          "title": "Thông tin chung"
        },
        {
          "type": "FormSection",
          "span": 6,
          "formKey": "phieu-bao-hiem",
          "title": "Bảo hiểm"
        }
      ]
    },
    {
      "components": [
        {
          "type": "Divider",
          "span": 12,
          "label": "Ghi chú"
        },
        {
          "type": "TextBlock",
          "span": 12,
          "content": "Vui lòng điền đầy đủ thông tin",
          "align": "center"
        }
      ]
    }
  ]
}
```

---

### `GET /forms/pages/{moduleCode}/{pageCode}`

> Lấy page schema đã hydrate — form được nhúng đầy đủ schema.  
> Auth: `[AllowAnonymous]`  
> Chỉ trả về page `Published`.

**Hydration:** Mỗi `FormSectionPageComponent` được resolve thành `FormSectionPageComponentDto` chứa `Schema: FormSchemaDto` đầy đủ. Các form trong component MUST đang `Published`; nếu không — component vẫn trả về nhưng `Schema = null`.

---

## 6. Integration Events

### Event: `FormSubmittedIntegrationEvent`

> Publish khi user submit form thành công.

| Field | Type | Mô tả |
|-------|------|-------|
| `SubmissionId` | `Guid` | ID của `FormSubmission` |
| `FormTemplateId` | `Guid` | ID của form |
| `ModuleCode` | `string` | Module slug |
| `FormKey` | `string` | Form slug |
| `SubmittedBy` | `Guid?` | UserID, null nếu anonymous |

**Publisher:** `DynamicFormService` — qua MassTransit EntityFrameworkOutbox (PostgreSQL)  
**Consumer hiện tại:** Chưa có — sẵn sàng để service khác subscribe (vd: NotificationService gửi xác nhận, AuditService ghi log).

---

## 7. Business Rules tổng hợp

| # | Rule | Enforce bởi |
|---|------|------------|
| **BR-01** | `FormModule.Code` MUST unique global | `ExistsByCodeAsync` trong handler |
| **BR-02** | `FormTemplate.Key` MUST unique trong module | `ExistsByKeyInModuleAsync` |
| **BR-03** | `FormField.Key` MUST unique trong form | Guard trong `FormTemplate.AddField()` |
| **BR-04** | `FormPage.Code` MUST unique trong module | `ExistsByCodeAsync` |
| **BR-05** | Không được thêm/sửa field vào form `Published` hoặc `Archived` | Guard trong `FormTemplate.AddField()` |
| **BR-06** | Không được `Update` form `Published` hoặc `Archived` | Guard trong `FormTemplate.Update()` |
| **BR-07** | `Publish()` form MUST có ≥ 1 field | Guard trong `FormTemplate.Publish()` |
| **BR-08** | Chỉ nhận submission khi form `Published` | Check trong `SubmitFormCommandHandler` |
| **BR-09** | `FormPage.UpdateLayout()` và `Publish()` MUST NOT khi `Archived` | Guard trong `FormPage` |
| **BR-10** | `FormSubmission` immutable sau khi tạo — chỉ `Status` thay đổi | Không có method sửa answers |
| **BR-11** | `FormVersion` capture tại thời điểm submit — không thay đổi dù form publish lại | Set trong `FormSubmission.Create()` |

---

## 8. Validation Rules tổng hợp

| Field | Entity/Command | Pattern / Constraint |
|-------|----------------|---------------------|
| `Module.Code` | `CreateModuleCommand` | NotEmpty, max 50, `^[a-z0-9\-]+$` |
| `Form.Key` | `CreateFormCommand` | NotEmpty, max 100, `^[a-z0-9\-]+$` |
| `Field.Key` | `AddFieldCommand` | NotEmpty, max 100, `^[a-z0-9_]+$` ← dùng `_` không phải `-` |
| `Page.Code` | `CreatePageCommand` | NotEmpty, max 100, `^[a-z0-9\-]+$` |
| `Module.Name` | `CreateModuleCommand` | NotEmpty, max 200 |
| `Form.Name` | `CreateFormCommand` | NotEmpty, max 200 |
| `Field.Label` | `AddFieldCommand` | NotEmpty, max 200 |
| `Field.Order` | `AddFieldCommand` | `≥ 0` |
| `Submission.Page` | `GetSubmissionsQuery` | `> 0` |
| `Submission.PageSize` | `GetSubmissionsQuery` | 1–100 |
| `ConditionalLogic.Operator` | — | `"Equals"` \| `"NotEquals"` \| `"Contains"` |
| `ConditionalLogic.Action` | — | `"Show"` \| `"Hide"` |
| `ValidationRule.Type` | — | `"required"` \| `"minLength"` \| `"maxLength"` \| `"pattern"` \| `"min"` \| `"max"` |
