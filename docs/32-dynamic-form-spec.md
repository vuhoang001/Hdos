# 32 — DynamicFormService: Technical Specification & API Reference

> **Loại doc:** Technical Spec + API Walkthrough — viết theo format [00-spec-format.md](./00-spec-format.md).  
> **Dùng để:** AI implement feature mới, onboard dev, review PR, frontend integration.

---

## Mục lục

1. [Enums](#1-enums)
2. [Value Objects](#2-value-objects)
3. [Entities](#3-entities)
4. [Repositories](#4-repositories)
5. [API Endpoints (Spec)](#5-api-endpoints)
6. [Integration Events](#6-integration-events)
7. [Business Rules tổng hợp](#7-business-rules-tổng-hợp)
8. [Validation Rules tổng hợp](#8-validation-rules-tổng-hợp)
9. [API Walkthrough](#9-api-walkthrough)
10. [Bảng tham chiếu nhanh](#10-bảng-tham-chiếu-nhanh)

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

| Int | Name | Mô tả | Cần `Options`? |
|-----|------|-------|----------------|
| 0 | `Text` | Input text một dòng | Không |
| 1 | `Textarea` | Input text nhiều dòng | Không |
| 2 | `Number` | Input số | Không |
| 3 | `Date` | Chọn ngày (date picker) — ISO 8601 date | Không |
| 4 | `DateTime` | Chọn ngày giờ — ISO 8601 datetime | Không |
| 5 | `Select` | Dropdown chọn một | **SHOULD** |
| 6 | `MultiSelect` | Dropdown chọn nhiều — trả về mảng string | **SHOULD** |
| 7 | `Radio` | Radio buttons | **SHOULD** |
| 8 | `Checkbox` | Single checkbox — trả về `"true"` / `"false"` | Không |
| 9 | `File` | Upload file | Không |
| 10 | `Signature` | Ký tên (canvas) — trả về base64 PNG | Không |
| 11 | `Section` | Tiêu đề phân cách, không nhập liệu | Không |

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

| Field | Type | Constraint |
|-------|------|-----------|
| `Type` | `string` | MUST một trong: `required`, `minLength`, `maxLength`, `pattern`, `min`, `max` |
| `Value` | `string` | MUST NotEmpty |
| `ErrorMessage` | `string` | MUST NotEmpty |

**Bảng `Type`:**

| Type | Áp dụng cho FieldType | `Value` là |
|------|----------------------|-----------|
| `required` | Tất cả | `"true"` |
| `minLength` | `Text`, `Textarea` | số nguyên dương |
| `maxLength` | `Text`, `Textarea` | số nguyên dương |
| `pattern` | `Text` | regex string |
| `min` | `Number`, `Date`, `DateTime` | số / ISO date |
| `max` | `Number`, `Date`, `DateTime` | số / ISO date |

---

### Value Object: `ConditionalLogic`

> Logic hiển thị/ẩn field dựa trên giá trị field khác. Lưu dạng `[JSONB]` trong `FormField.ConditionalLogicJson` (single object).

| Field | Type | Constraint |
|-------|------|-----------|
| `SourceFieldKey` | `string` | MUST NotEmpty; MUST là key của field khác trong cùng form |
| `Operator` | `string` | MUST một trong: `"Equals"`, `"NotEquals"`, `"Contains"` |
| `Value` | `string` | MUST NotEmpty |
| `Action` | `string` | MUST một trong: `"Show"`, `"Hide"` |

**Ví dụ:** Hiện field `diabetes_type` khi field `has_diabetes` = `"true"`:
```json
{ "sourceFieldKey": "has_diabetes", "operator": "Equals", "value": "true", "action": "Show" }
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

| Field | Type | Constraint |
|-------|------|-----------|
| `FieldKey` | `string` | MUST NotEmpty |
| `Value` | `string?` | MAY null — null nếu bỏ qua field; mảng serialize thành JSON string |

---

### Value Object: `FormPageLayout`

> Layout của `FormPage`. Lưu dạng `[JSONB]` trong `FormPage.LayoutJson`. Default khi tạo: `{"rows":[]}`.

**FormPageComponent** — polymorphic, phân biệt bằng discriminator field `"type"`:

| `"type"` | C# Type | Fields bắt buộc | Mô tả |
|----------|---------|----------------|-------|
| `"FormSection"` | `FormSectionPageComponent` | `FormKey: string` | Nhúng form; `FormKey` MUST là key form đã Published |
| `"TextBlock"` | `TextBlockPageComponent` | `Content: string` | Khối văn bản; `Align` ∈ `"left"`, `"center"`, `"right"` |
| `"Divider"` | `DividerPageComponent` | — | Đường phân cách |

`Span` là số cột (1–12) trong grid. `null` = full width.

---

## 3. Entities

### Entity: `FormModule`

> Aggregate root. DB table: `FormModules`.

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK, `ValueGeneratedNever()` | — |
| `Code` | `string` | MUST unique global; max 50; `^[a-z0-9\-]+$` | [R] sau khi tạo |
| `Name` | `string` | MUST NotEmpty; max 200 | — |
| `Description` | `string?` | max 500; MAY null | — |
| `Status` | `ModuleStatus` | — | Default: `Active` |

**State Machine:**

```
──Create()──→ Active ──Deactivate()──→ Inactive ──Activate()──→ Active
```

---

### Entity: `FormTemplate`

> Aggregate root. DB table: `FormTemplates`. Chứa danh sách `FormField` (child entities).

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Id` | `Guid` | PK | — |
| `ModuleCode` | `string` | max 50 | [DENORM] copy từ Module.Code — tránh JOIN |
| `Key` | `string` | MUST unique trong module; max 100; `^[a-z0-9\-]+$` | [R] |
| `Status` | `FormStatus` | — | Default: `Draft` |
| `Version` | `int` | ≥ 1 | Tăng khi `Publish()` |
| `SettingsJson` | `string` | [JSONB] — `FormSettings` | — |

**State Machine:**

```
──Create()──→ Draft ──Publish()──→ Published ──Archive()──→ Archived
```

| Method | Precondition | Side Effect |
|--------|-------------|------------|
| `AddField(...)` | MUST NOT `Published` hoặc `Archived`; FieldKey MUST unique | — |
| `Publish()` | MUST có ≥ 1 field; MUST `Draft` | Raise `FormPublishedDomainEvent`; `Version++` |
| `Archive()` | MUST `Published` | `Status = Archived` |

---

### Entity: `FormField`

> Child entity của `FormTemplate`. DB table: `FormFields`.

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Key` | `string` | MUST unique trong form; max 100; `^[a-z0-9_]+$` | Dùng `_` thay `-` |
| `Label` | `string` | MUST NotEmpty; max 200 | — |
| `FieldType` | `FieldType` | — | Xem Enum bên trên |
| `Order` | `int` | ≥ 0 | Thứ tự render |
| `Width` | `FieldWidth` | — | Default: `Full` |
| `OptionsJson` | `string?` | [JSONB] — `List<FieldOption>` | SHOULD có nếu Select/MultiSelect/Radio |
| `ValidationRulesJson` | `string?` | [JSONB] — `List<ValidationRule>` | — |
| `ConditionalLogicJson` | `string?` | [JSONB] — `ConditionalLogic` | Single object |

---

### Entity: `FormSubmission`

> Aggregate root. DB table: `FormSubmissions`. Immutable sau khi tạo — chỉ `Status` thay đổi.

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `FormVersion` | `int` | — | Capture version tại thời điểm submit |
| `SubmittedBy` | `Guid?` | MAY null | null nếu anonymous |
| `AnswersJson` | `string` | [JSONB] — `List<FieldAnswer>` | — |

**Side Effect:** `Create(...)` → Raise `FormSubmittedDomainEvent`

---

### Entity: `FormPage`

> Aggregate root. DB table: `FormPages`. Kết hợp nhiều form vào một màn hình layout.

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Code` | `string` | MUST unique trong module; max 100; `^[a-z0-9\-]+$` | [R] |
| `Status` | `FormPageStatus` | — | Default: `Draft` |
| `LayoutJson` | `string` | [JSONB] — `FormPageLayout` | Default: `{"rows":[]}` |

**State Machine:**

```
──Create()──→ Draft ──Publish()──→ Published ──Archive()──→ Archived
```

| Method | Precondition |
|--------|-------------|
| `UpdateLayout(layoutJson)` | MUST NOT `Archived` |
| `Publish()` | MUST NOT `Archived` |

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
| `GetByFormTemplateAsync(formTemplateId, page, pageSize, ct)` | `List<FormSubmission>` | Ordered by `SubmittedAt DESC` |
| `CountByFormTemplateAsync(formTemplateId, ct)` | `int` | Tổng số submission cho pagination |
| `AddAsync(submission, ct)` | `void` | Chưa commit |

### `IFormPageRepository`

| Method | Trả về | Ghi chú |
|--------|--------|---------|
| `GetByCodeAsync(moduleCode, pageCode, ct)` | `FormPage?` | Key = `(moduleCode, pageCode)` |
| `GetByModuleAsync(moduleCode, ct)` | `List<FormPage>` | — |
| `ExistsByCodeAsync(moduleCode, pageCode, ct)` | `bool` | — |
| `Add(page)` | `void` | Sync, chưa commit |

---

## 5. API Endpoints

> Base URL (qua nginx): `https://<host>/forms`  
> Swagger UI: `https://<host>/forms/swagger`

### `GET /forms/modules` — `[AllowAnonymous]`

Lấy danh sách tất cả module đang Active.

**Response 200:** `List<ModuleDto>` — `{ id, code, name, description, status:"active", formCount, createdAtUtc }`

---

### `POST /forms/admin/modules` — `[Authorize(Roles="admin")]`

Tạo module mới.

**Validation:**

| Field | Rule |
|-------|------|
| `code` | NotEmpty, max 50, `^[a-z0-9\-]+$`, MUST unique global |
| `name` | NotEmpty, max 200 |

| Code | Khi nào |
|------|---------|
| 201 | Tạo thành công |
| 409 | `code` đã tồn tại |

---

### `POST /forms/admin/modules/{moduleCode}/forms` — `[Authorize(Roles="admin")]`

Tạo form mới trong module.

**Validation:**

| Field | Rule |
|-------|------|
| `key` | NotEmpty, max 100, `^[a-z0-9\-]+$`, MUST unique trong module |
| `moduleCode` (route) | MUST là module tồn tại và Active |

| Code | Khi nào |
|------|---------|
| 200 | Tạo thành công |
| 400 | Module không tồn tại |
| 409 | Key đã tồn tại trong module |

---

### `POST /forms/admin/forms/{formTemplateId}/fields` — `[Authorize(Roles="admin")]`

Thêm field vào form. MUST NOT form đã `Published`.

**Validation:**

| Field | Rule |
|-------|------|
| `key` | NotEmpty, max 100, `^[a-z0-9_]+$` ← dùng `_` không phải `-`, MUST unique trong form |
| `fieldType` | MUST là int hợp lệ trong `FieldType` (0–11) |
| `width` | MUST là int hợp lệ trong `FieldWidth` (0–2) |
| Form | MUST NOT `Published` hoặc `Archived` |

| Code | Khi nào |
|------|---------|
| 200 | Thêm thành công |
| 400 | Form Published; field key trùng |
| 404 | Form không tồn tại |

---

### `POST /forms/admin/forms/{formTemplateId}/publish` — `[Authorize(Roles="admin")]`

Publish form. Form MUST có ≥ 1 field.

**Side Effects:** Raise `FormPublishedDomainEvent`; `Version` tăng lên 1.

---

### `POST /forms/admin/forms/{formTemplateId}/archive` — `[Authorize(Roles="admin")]`

Archive form. Form MUST đang `Published`.

---

### `GET /forms/{moduleCode}/{formKey}/schema` — `[AllowAnonymous]`

Lấy schema BDUI của form. Chỉ trả về form `Published`.

**Response 200:**
```json
{
  "id": "guid", "moduleCode": "tiep-nhan", "formKey": "phieu-tiep-nhan",
  "name": "Phiếu tiếp nhận", "version": 1,
  "fields": [
    {
      "id": "guid", "key": "ho_ten", "label": "Họ và tên",
      "type": "text", "order": 0, "required": true, "width": "full",
      "placeholder": "Nhập họ tên", "helpText": null,
      "options": null, "validationRules": [...], "conditionalLogic": null
    }
  ],
  "settings": { "submitButtonLabel": "Gửi phiếu", "successMessage": "Đã gửi", "allowMultipleSubmissions": true }
}
```

| Code | Khi nào |
|------|---------|
| 200 | Form tồn tại và Published |
| 404 | Form không tồn tại hoặc chưa/không còn Published |

---

### `POST /forms/{moduleCode}/{formKey}/submit` — `[AllowAnonymous]`

Submit form. Form MUST đang `Published`. `SubmittedBy` lấy từ JWT `sub` claim nếu có.

**Body:**
```json
{
  "answers": [
    { "fieldKey": "ho_ten", "value": "Nguyễn Văn A" },
    { "fieldKey": "ngay_sinh", "value": "1990-05-15" }
  ]
}
```

**Side Effects:** Raise `FormSubmittedDomainEvent` → Publish `FormSubmittedIntegrationEvent` qua MassTransit outbox.

| Code | Khi nào |
|------|---------|
| 200 | Submit thành công — `{ "submissionId": "guid" }` |
| 400 | Form chưa Published hoặc Archived |
| 404 | Form không tồn tại |

---

### `GET /forms/admin/forms/{formTemplateId}/submissions` — `[Authorize(Roles="admin")]`

Danh sách submission có phân trang.

**Query params:** `page` (default 1, > 0), `pageSize` (default 20, 1–100)

---

### `POST /forms/admin/modules/{moduleCode}/pages` — `[Authorize(Roles="admin")]`

Tạo page layout mới. `code` MUST unique trong module, `^[a-z0-9\-]+$`, max 100.

---

### `PUT /forms/admin/pages/{pageId}/layout` — `[Authorize(Roles="admin")]`

Cập nhật layout JSON của page. MUST NOT page `Archived`.

---

### `POST /forms/admin/pages/{pageId}/publish` — `[Authorize(Roles="admin")]`

Publish page.

---

### `GET /forms/pages/{moduleCode}/{pageCode}` — `[AllowAnonymous]`

Lấy page schema đã hydrate — mỗi `FormSectionPageComponent` được resolve thành schema đầy đủ của form. Chỉ trả về page `Published`. Nếu form trong component chưa `Published` — component vẫn trả về nhưng `Schema = null`.

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
**Consumer hiện tại:** Chưa có — sẵn sàng để service khác subscribe (vd: NotificationService gửi xác nhận).

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

---

## 9. API Walkthrough

> Hướng dẫn từng bước để frontend developer / tester tích hợp. Dùng ví dụ thực tế: màn hình "Tiếp nhận bệnh nhân" gồm 2 form.

### Quy tắc chung

**Response format:**
```jsonc
// Thành công
{ "success": true, "data": { ... } }

// Lỗi
{ "success": false, "error": { "code": "NotFound", "message": "Module 'tiep-nhan' không tồn tại" } }
```

**Quy tắc đặt `code` / `key`:**
- Module/Form/Page code: chữ **thường**, số, dấu gạch ngang `-` (vd: `tiep-nhan`, `phieu-tiep-nhan`)
- Field key: chữ thường, số, dấu gạch **dưới** `_` (vd: `ho_ten`, `ngay_sinh`)

**Vòng đời:**
```
Form/Page:   Draft → Published → Archived
Module:      Active / Inactive
```

### Ví dụ: Màn hình "Tiếp nhận bệnh nhân"

```
[Thông tin BN - 8 cột] [Bảo hiểm - 4 cột]
[Ghi chú: Kiểm tra trước khi lưu - 12 cột]
```

**Bước 1 — Tạo module**

```http
POST /forms/admin/modules
{ "code": "tiep-nhan", "name": "Tiếp nhận bệnh nhân", "description": "Quản lý phiếu tiếp nhận" }
```

**Bước 2 — Tạo form "Phiếu tiếp nhận"**

```http
POST /forms/admin/modules/tiep-nhan/forms
{
  "key": "phieu-tiep-nhan",
  "name": "Phiếu Tiếp Nhận",
  "submitButtonLabel": "Lưu phiếu",
  "allowMultipleSubmissions": true
}
```
→ lưu `id` = `{formA-id}`

**Bước 3 — Thêm field vào form A**

```http
POST /forms/admin/forms/{formA-id}/fields
{ "key": "ho_ten",    "label": "Họ và tên",  "fieldType": 0, "order": 1, "required": true,  "width": 1 }

POST /forms/admin/forms/{formA-id}/fields
{ "key": "ngay_sinh", "label": "Ngày sinh",  "fieldType": 3, "order": 2, "required": true,  "width": 1 }

POST /forms/admin/forms/{formA-id}/fields
{
  "key": "gioi_tinh", "label": "Giới tính", "fieldType": 5, "order": 3, "required": true, "width": 1,
  "options": [{"label":"Nam","value":"male"},{"label":"Nữ","value":"female"},{"label":"Khác","value":"other"}]
}

POST /forms/admin/forms/{formA-id}/fields
{
  "key": "so_dien_thoai", "label": "Số điện thoại", "fieldType": 0, "order": 4, "required": true, "width": 1,
  "validationRules": [
    { "type": "pattern", "value": "^(0|\\+84)[0-9]{9}$", "errorMessage": "Số điện thoại không hợp lệ" }
  ]
}
```

**Bước 4 — Publish form A**

```http
POST /forms/admin/forms/{formA-id}/publish
```

**Bước 5 — Tạo + thêm field + publish form "Phiếu bảo hiểm"**

```http
POST /forms/admin/modules/tiep-nhan/forms
{ "key": "phieu-bao-hiem", "name": "Phiếu Bảo Hiểm" }
→ {formB-id}

POST /forms/admin/forms/{formB-id}/fields
{ "key": "ma_the_bhyt", "label": "Mã thẻ BHYT", "fieldType": 0, "order": 1, "required": true, "width": 0 }

POST /forms/admin/forms/{formB-id}/fields
{ "key": "noi_dang_ky", "label": "Nơi đăng ký KCB", "fieldType": 0, "order": 2, "required": true, "width": 0 }

POST /forms/admin/forms/{formB-id}/publish
```

**Bước 6 — Tạo Page**

```http
POST /forms/admin/modules/tiep-nhan/pages
{ "code": "man-hinh-tiep-nhan", "title": "Màn hình Tiếp nhận" }
→ {pageId}
```

**Bước 7 — Cài layout**

```http
PUT /forms/admin/pages/{pageId}/layout
{
  "rows": [
    { "components": [
        { "type": "FormSection", "span": 8, "formKey": "phieu-tiep-nhan", "title": "Thông tin bệnh nhân" },
        { "type": "FormSection", "span": 4, "formKey": "phieu-bao-hiem" }
    ]},
    { "components": [
        { "type": "TextBlock", "span": 12, "content": "Kiểm tra kỹ trước khi lưu", "align": "center" }
    ]}
  ]
}
```

**Bước 8 — Publish Page**

```http
POST /forms/admin/pages/{pageId}/publish
```

**Bước 9 — Frontend gọi 1 request để lấy toàn bộ layout**

```http
GET /forms/pages/tiep-nhan/man-hinh-tiep-nhan
```

→ Nhận toàn bộ layout + schema của mỗi form → render màn hình → submit từng form riêng:

```http
POST /forms/tiep-nhan/phieu-tiep-nhan/submit
{ "answers": [{"fieldKey":"ho_ten","value":"Nguyễn Văn A"}, {"fieldKey":"ngay_sinh","value":"1990-05-15"}] }

POST /forms/tiep-nhan/phieu-bao-hiem/submit
{ "answers": [{"fieldKey":"ma_the_bhyt","value":"DN4050012345"}, {"fieldKey":"noi_dang_ky","value":"Bệnh viện Đà Nẵng"}] }
```

### Field có ConditionalLogic (hiển thị theo điều kiện)

```json
{
  "key": "ten_nguoi_bao_lanh",
  "label": "Tên người bảo lãnh",
  "fieldType": 0, "order": 7, "required": false, "width": 1,
  "conditionalLogic": {
    "sourceFieldKey": "co_nguoi_bao_lanh",
    "operator": "Equals",
    "value": "yes",
    "action": "Show"
  }
}
```

*"Hiện field `ten_nguoi_bao_lanh` khi field `co_nguoi_bao_lanh` bằng `yes`"*

---

## 10. Bảng tham chiếu nhanh

| Method | URL | Mô tả | Auth |
|--------|-----|-------|------|
| `POST` | `/forms/admin/modules` | Tạo module | Admin |
| `GET` | `/forms/modules` | Danh sách module | Public |
| `POST` | `/forms/admin/modules/{code}/forms` | Tạo form | Admin |
| `GET` | `/forms/{moduleCode}` | Danh sách form | Public |
| `POST` | `/forms/admin/forms/{id}/fields` | Thêm field | Admin |
| `POST` | `/forms/admin/forms/{id}/publish` | Publish form | Admin |
| `POST` | `/forms/admin/forms/{id}/archive` | Archive form | Admin |
| `GET` | `/forms/{moduleCode}/{formKey}/schema` | Schema BDUI | Public |
| `POST` | `/forms/{moduleCode}/{formKey}/submit` | Submit form | Public |
| `GET` | `/forms/admin/forms/{id}/submissions` | Danh sách submission | Admin |
| `POST` | `/forms/admin/modules/{code}/pages` | Tạo page | Admin |
| `PUT` | `/forms/admin/pages/{id}/layout` | Cài layout | Admin |
| `POST` | `/forms/admin/pages/{id}/publish` | Publish page | Admin |
| `GET` | `/forms/pages/{moduleCode}` | Danh sách page | Public |
| `GET` | `/forms/pages/{moduleCode}/{pageCode}` | Schema page BDUI | Public |
| `GET` | `/forms/health` | Health check | Public |

### Ràng buộc quan trọng

| Ràng buộc | Chi tiết |
|-----------|---------|
| Thêm field | Chỉ khi form đang `Draft` |
| Publish form | Phải có ít nhất 1 field |
| Đọc schema / submit | Form phải `Published` |
| Đọc page schema | Page phải `Published` + mỗi form trong layout phải `Published` |
| FormSection trong Page | `formKey` phải là form cùng module, đã Published |
| Span tổng mỗi row | Nên ≤ 12 (vượt quá frontend tự xuống dòng theo CSS) |
| Field key | Dùng `_` (gạch dưới), không phải `-` (gạch ngang) |
