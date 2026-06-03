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

### Enum: `WidgetType` ⭐ Screen Designer

> Loại widget trong `FormScreenWidget`. Quyết định cách frontend render widget trên canvas.

| Name | Mô tả | `configJson` fields | `referenceId` |
|------|-------|---------------------|---------------|
| `FormSection` | Nhúng một `FormTemplate` vào canvas | — | `FormTemplate.Id` (MUST) |
| `TextBlock` | Khối văn bản / markdown | `content: string`, `align: "left"\|"center"\|"right"` | null |
| `Divider` | Đường ngang phân cách | `label?: string` | null |
| `ImageBlock` | Hình ảnh tĩnh | `url: string`, `alt?: string` | null |
| `ConditionalSection` | Container ẩn/hiện theo điều kiện | `condition: {fieldKey, operator, value}` | null |

**Serialize JSON:** tên enum nguyên gốc PascalCase (vd: `"FormSection"`)

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

### Entity: `FormScreen` ⭐ Screen Designer

> Aggregate root. DB table: `FormScreens`. Màn hình SDUI thiết kế bằng drag-and-drop. Chứa danh sách `FormScreenTab`.

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `Code` | `string` | MUST unique trong module; max 100; `^[a-z0-9\-]+$` | [R] sau khi tạo |
| `Title` | `string` | MUST NotEmpty; max 200 | — |
| `Status` | `FormStatus` | — | Default: `Draft` |
| `SortOrder` | `int` | ≥ 0 | Thứ tự hiển thị trong sidebar |

**State Machine:**

```
──Create()──→ Draft ──Publish()──→ Published ──Archive()──→ Archived
```

| Method | Precondition | Side Effect |
|--------|-------------|------------|
| `AddTab(label, slug, sortOrder, isDefault)` | MUST NOT `Archived`; slug MUST unique trong screen | Thêm tab vào `_tabs` |
| `RemoveTab(tabId)` | Tab MUST tồn tại | Xóa tab khỏi `_tabs` (cascade xóa widgets) |
| `Publish()` | MUST NOT `Archived` | Raise `FormScreenPublishedDomainEvent` |

---

### Entity: `FormScreenTab` ⭐ Screen Designer

> Child entity của `FormScreen` (BaseEntity). DB table: `FormScreenTabs`.

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `ScreenId` | `Guid` | FK → `FormScreens.Id` | — |
| `Label` | `string` | MUST NotEmpty; max 200 | — |
| `Slug` | `string` | MUST unique trong screen; max 100; `^[a-z0-9\-]+$` | [R] |
| `SortOrder` | `int` | ≥ 0 | Thứ tự hiển thị |
| `IsDefault` | `bool` | — | Tab được chọn khi mở screen |

| Method | Ghi chú |
|--------|---------|
| `Update(label, sortOrder, isDefault)` | Cập nhật metadata |
| `ReplaceWidgets(widgets)` | Full replacement — xóa cũ, thêm mới. Gọi bởi `SaveTabWidgetsCommand` |

---

### Entity: `FormScreenWidget` ⭐ Screen Designer

> Child entity của `FormScreenTab` (BaseEntity). DB table: `FormScreenWidgets`.

| Field | Type | Constraint | Ghi chú |
|-------|------|-----------|---------|
| `TabId` | `Guid` | FK → `FormScreenTabs.Id` | — |
| `WidgetKey` | `string` | MUST unique trong tab; max 100 | Định danh widget trên canvas |
| `WidgetType` | `WidgetType` | — | Loại widget |
| `GridX` | `int` | ≥ 0 | Cột trên grid (react-grid-layout) |
| `GridY` | `int` | ≥ 0 | Hàng trên grid |
| `GridW` | `int` | ≥ 1; default 6 | Chiều rộng (số cột) |
| `GridH` | `int` | ≥ 1; default 4 | Chiều cao (số hàng) |
| `ConfigJson` | `string` | [JSONB]; default `{}` | Cấu hình hiển thị tuỳ `WidgetType` |
| `ReferenceId` | `Guid?` | MAY null | `FormTemplate.Id` khi `WidgetType = FormSection` |

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

### `IFormScreenRepository` ⭐

| Method | Trả về | Ghi chú |
|--------|--------|---------|
| `GetByIdAsync(id, ct)` | `FormScreen?` | — |
| `GetByCodeAsync(moduleCode, screenCode, ct)` | `FormScreen?` | Không include tabs |
| `GetByCodeWithTabsAsync(moduleCode, screenCode, ct)` | `FormScreen?` | Include tabs + widgets — dùng khi cần AddTab/RemoveTab |
| `GetWithTabsAndWidgetsAsync(moduleCode, screenCode, ct)` | `FormScreen?` | Chỉ trả `Published`; dùng cho SDUI layout |
| `GetByModuleAsync(moduleCode, ct)` | `List<FormScreen>` | Ordered by `SortOrder` |
| `ExistsByCodeAsync(moduleCode, screenCode, ct)` | `bool` | — |
| `GetTabWithWidgetsAsync(screenId, tabId, ct)` | `FormScreenTab?` | Include widgets; dùng cho `SaveTabWidgetsCommand` |
| `Add(screen)` | `void` | Sync, chưa commit |
| `Remove(screen)` | `void` | Cascade xóa tabs + widgets |

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

### `GET /forms/admin/widget-catalog` — `[Authorize(Roles="admin")]` ⭐

Lấy danh sách widget types hỗ trợ. Dùng để populate palette trong designer.

**Response 200:** `List<WidgetCatalogItemDto>` — `{ widgetType, label, description, defaultW, defaultH }`

---

### `GET /forms/admin/screens/{moduleCode}` — `[Authorize(Roles="admin")]` ⭐

Danh sách tất cả screens (mọi status) của module, sắp xếp theo `SortOrder`.

---

### `POST /forms/admin/screens` — `[Authorize(Roles="admin")]` ⭐

Tạo screen mới. Module MUST tồn tại.

**Validation:**

| Field | Rule |
|-------|------|
| `moduleCode` | MUST là module tồn tại |
| `code` | NotEmpty, max 100, `^[a-z0-9\-]+$`, MUST unique trong module |
| `title` | NotEmpty, max 200 |

| Code | Khi nào |
|------|---------|
| 201 | Tạo thành công |
| 400 | Module không tồn tại |
| 409 | `code` đã tồn tại trong module |

---

### `PUT /forms/admin/screens/{moduleCode}/{screenCode}` — `[Authorize(Roles="admin")]` ⭐

Cập nhật `title`, `description`, `sortOrder`. MUST NOT screen `Archived`.

---

### `DELETE /forms/admin/screens/{moduleCode}/{screenCode}` — `[Authorize(Roles="admin")]` ⭐

Xóa screen. Cascade xóa tất cả tabs và widgets.

**Response `204 No Content`**

---

### `POST /forms/admin/screens/{moduleCode}/{screenCode}/publish` — `[Authorize(Roles="admin")]` ⭐

Publish screen. MUST NOT `Archived`. Raise `FormScreenPublishedDomainEvent`.

---

### `POST /forms/admin/screens/{moduleCode}/{screenCode}/tabs` — `[Authorize(Roles="admin")]` ⭐

Thêm tab vào screen.

**Validation:**

| Field | Rule |
|-------|------|
| `label` | NotEmpty, max 200 |
| `slug` | NotEmpty, max 100, `^[a-z0-9\-]+$`, MUST unique trong screen |

---

### `PUT /forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` — `[Authorize(Roles="admin")]` ⭐

Cập nhật tab. Tab MUST thuộc screen này.

---

### `DELETE /forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` — `[Authorize(Roles="admin")]` ⭐

Xóa tab. Cascade xóa tất cả widgets trong tab.

---

### `PUT /forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}/widgets` — `[Authorize(Roles="admin")]` ⭐

**Drag-and-drop save — endpoint chính của designer.** Lưu toàn bộ canvas cho một tab (full replacement — xóa cũ, insert mới trong một transaction).

**Body:** `List<WidgetInput>`

```json
[
  {
    "widgetKey": "patient-form",
    "widgetType": "FormSection",
    "gridX": 0, "gridY": 0, "gridW": 8, "gridH": 10,
    "configJson": "{}",
    "referenceId": "uuid-of-form-template"
  },
  {
    "widgetKey": "intro-text",
    "widgetType": "TextBlock",
    "gridX": 8, "gridY": 0, "gridW": 4, "gridH": 3,
    "configJson": "{\"content\": \"Chào mừng!\", \"align\": \"center\"}",
    "referenceId": null
  }
]
```

**Validation:**

| Rule | Chi tiết |
|------|---------|
| `widgetKey` | NotEmpty, max 100; MUST unique trong request |
| `widgetType` | MUST là `WidgetType` enum hợp lệ |
| `gridW`, `gridH` | `> 0` |

**Response 200:** `{ "saved": <số widget> }`

| Code | Khi nào |
|------|---------|
| 200 | Lưu thành công |
| 400 | Validation fail (widgetKey trùng, widgetType không hợp lệ...) |
| 404 | Screen hoặc Tab không tồn tại |

---

### `GET /forms/screens/{moduleCode}` — `[AllowAnonymous]` ⭐

Danh sách screens của module. Trả tất cả status (frontend tự filter nếu cần).

---

### `GET /forms/screens/{moduleCode}/{screenCode}/layout` — `[AllowAnonymous]` ⭐

**SDUI endpoint chính.** Lấy toàn bộ layout của screen (tabs + widgets). Chỉ trả về screen `Published`. Với mỗi widget `FormSection`, tự động hydrate `formSchema` từ `FormTemplate` tương ứng.

**Response 200:**

```json
{
  "id": "uuid",
  "moduleCode": "tiep-nhan",
  "code": "man-hinh-tiep-nhan",
  "title": "Màn hình tiếp nhận",
  "tabs": [
    {
      "id": "uuid",
      "label": "Thông tin BN",
      "slug": "thong-tin-bn",
      "isDefault": true,
      "sortOrder": 0,
      "widgets": [
        {
          "widgetKey": "patient-form",
          "widgetType": "FormSection",
          "gridX": 0, "gridY": 0, "gridW": 8, "gridH": 10,
          "config": null,
          "referenceId": "uuid",
          "formSchema": { "formKey": "...", "fields": [...], "settings": {...} }
        }
      ]
    }
  ],
  "generatedAt": "2026-06-03T..."
}
```

| Code | Khi nào |
|------|---------|
| 200 | Screen tồn tại và `Published` |
| 404 | Screen không tồn tại hoặc chưa `Published` |

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
| **BR-04** | `FormScreen.Code` MUST unique trong module | `ExistsByCodeAsync` trong handler |
| **BR-05** | Không được thêm/sửa field vào form `Published` hoặc `Archived` | Guard trong `FormTemplate.AddField()` |
| **BR-06** | Không được `Update` form `Published` hoặc `Archived` | Guard trong `FormTemplate.Update()` |
| **BR-07** | `Publish()` form MUST có ≥ 1 field | Guard trong `FormTemplate.Publish()` |
| **BR-08** | Chỉ nhận submission khi form `Published` | Check trong `SubmitFormCommandHandler` |
| **BR-09** | `FormScreen.AddTab()` và `Publish()` MUST NOT khi `Archived` | Guard trong `FormScreen` |
| **BR-10** | `FormScreenTab.Slug` MUST unique trong screen | Guard trong `FormScreen.AddTab()` |
| **BR-11** | `WidgetKey` MUST unique trong tab | Validation trong `SaveTabWidgetsCommandValidator` |
| **BR-12** | `FormSubmission` immutable sau khi tạo — chỉ `Status` thay đổi | Không có method sửa answers |
| **BR-13** | `FormVersion` capture tại thời điểm submit — không thay đổi dù form publish lại | Set trong `FormSubmission.Create()` |
| **BR-14** | `SaveTabWidgets` là full replacement — không patch từng widget | Gọi `tab.ReplaceWidgets(newWidgets)` |

---

## 8. Validation Rules tổng hợp

| Field | Entity/Command | Pattern / Constraint |
|-------|----------------|---------------------|
| `Module.Code` | `CreateModuleCommand` | NotEmpty, max 50, `^[a-z0-9\-]+$` |
| `Form.Key` | `CreateFormCommand` | NotEmpty, max 100, `^[a-z0-9\-]+$` |
| `Field.Key` | `AddFieldCommand` | NotEmpty, max 100, `^[a-z0-9_]+$` ← dùng `_` không phải `-` |
| `Screen.Code` | `CreateScreenCommand` | NotEmpty, max 100, `^[a-z0-9\-]+$` |
| `Tab.Slug` | `CreateTabCommand` | NotEmpty, max 100, `^[a-z0-9\-]+$` |
| `Widget.WidgetKey` | `SaveTabWidgetsCommand` | NotEmpty, max 100; unique trong request |
| `Widget.WidgetType` | `SaveTabWidgetsCommand` | MUST là `WidgetType` enum hợp lệ (PascalCase) |
| `Widget.GridW/H` | `SaveTabWidgetsCommand` | `> 0` |
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

**Bước 6 — Tạo Screen** ⭐ Screen Designer

```http
POST /forms/admin/screens
{ "moduleCode": "tiep-nhan", "code": "man-hinh-tiep-nhan", "title": "Màn hình Tiếp nhận", "sortOrder": 0 }
→ { "id": "{screenId}", "code": "man-hinh-tiep-nhan", ... }
```

**Bước 7 — Thêm Tab**

```http
POST /forms/admin/screens/tiep-nhan/man-hinh-tiep-nhan/tabs
{ "label": "Tiếp nhận", "slug": "tiep-nhan-tab", "sortOrder": 0, "isDefault": true }
→ { "id": "{tabId}", ... }
```

**Bước 8 — Lưu canvas (drag-and-drop)**

```http
PUT /forms/admin/screens/tiep-nhan/man-hinh-tiep-nhan/tabs/{tabId}/widgets
[
  {
    "widgetKey": "patient-form",
    "widgetType": "FormSection",
    "gridX": 0, "gridY": 0, "gridW": 8, "gridH": 10,
    "configJson": "{}",
    "referenceId": "{formA-id}"
  },
  {
    "widgetKey": "insurance-form",
    "widgetType": "FormSection",
    "gridX": 8, "gridY": 0, "gridW": 4, "gridH": 10,
    "configJson": "{}",
    "referenceId": "{formB-id}"
  },
  {
    "widgetKey": "note-text",
    "widgetType": "TextBlock",
    "gridX": 0, "gridY": 10, "gridW": 12, "gridH": 2,
    "configJson": "{\"content\": \"Kiểm tra kỹ trước khi lưu\", \"align\": \"center\"}",
    "referenceId": null
  }
]
→ { "saved": 3 }
```

**Bước 9 — Publish Screen**

```http
POST /forms/admin/screens/tiep-nhan/man-hinh-tiep-nhan/publish
```

**Bước 10 — Frontend gọi 1 request để lấy toàn bộ layout**

```http
GET /forms/screens/tiep-nhan/man-hinh-tiep-nhan/layout
```

→ Nhận toàn bộ layout (tabs + widgets + formSchema hydrated) → render màn hình → submit từng form riêng:

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
| `GET` | `/forms/admin/widget-catalog` | Danh sách widget types | Admin |
| `GET` | `/forms/admin/screens/{moduleCode}` | Danh sách screens | Admin |
| `POST` | `/forms/admin/screens` | Tạo screen | Admin |
| `PUT` | `/forms/admin/screens/{m}/{s}` | Cập nhật screen | Admin |
| `DELETE` | `/forms/admin/screens/{m}/{s}` | Xóa screen | Admin |
| `POST` | `/forms/admin/screens/{m}/{s}/publish` | Publish screen | Admin |
| `POST` | `/forms/admin/screens/{m}/{s}/tabs` | Thêm tab | Admin |
| `PUT` | `/forms/admin/screens/{m}/{s}/tabs/{id}` | Cập nhật tab | Admin |
| `DELETE` | `/forms/admin/screens/{m}/{s}/tabs/{id}` | Xóa tab | Admin |
| `PUT` | `/forms/admin/screens/{m}/{s}/tabs/{id}/widgets` | ⭐ Lưu canvas drag-drop | Admin |
| `GET` | `/forms/screens/{moduleCode}` | Danh sách screens public | Public |
| `GET` | `/forms/screens/{moduleCode}/{screenCode}/layout` | ⭐ SDUI layout | Public |
| `GET` | `/forms/health` | Health check | Public |

### Ràng buộc quan trọng

| Ràng buộc | Chi tiết |
|-----------|---------|
| Thêm field | Chỉ khi form đang `Draft` |
| Publish form | Phải có ít nhất 1 field |
| Đọc schema / submit | Form phải `Published` |
| Đọc screen layout | Screen phải `Published`; `FormSection` widget tự hydrate formSchema |
| `FormSection` widget | `referenceId` MUST là `FormTemplate.Id` hợp lệ |
| `SaveTabWidgets` | Full replacement — không patch; gửi toàn bộ canvas mỗi lần lưu |
| `WidgetKey` | MUST unique trong tab (validated client + server) |
| Field key | Dùng `_` (gạch dưới), không phải `-` (gạch ngang) |
| Screen code / Tab slug | Dùng `-` (gạch ngang), không phải `_` |
