# 30 — DynamicFormService: Tài liệu API chi tiết

> Base URL (qua nginx): `https://<host>/forms`  
> Swagger UI: `https://<host>/forms/swagger`

---

## Mục lục

1. [Tổng quan kiến trúc](#1-tổng-quan-kiến-trúc)
2. [Quy tắc chung](#2-quy-tắc-chung)
3. [Module API](#3-module-api)
4. [Form API](#4-form-api)
5. [Field API](#5-field-api)
6. [Form Schema (BDUI)](#6-form-schema-bdui)
7. [Submission API](#7-submission-api)
8. [Page API (multi-form layout)](#8-page-api-multi-form-layout)
9. [Luồng hoàn chỉnh — ví dụ thực tế](#9-luồng-hoàn-chỉnh--ví-dụ-thực-tế)
10. [Bảng tham chiếu nhanh](#10-bảng-tham-chiếu-nhanh)

---

## 1. Tổng quan kiến trúc

```
Module
  ├── Form (Template)
  │     └── Fields (các ô nhập liệu)
  └── Page
        └── Layout (JSON) — ghép nhiều Form vào một màn hình
```

| Khái niệm | Ý nghĩa |
|-----------|---------|
| **Module** | Nhóm logic nghiệp vụ, ví dụ: `tiep-nhan`, `kham-benh`, `noi-tru` |
| **Form** | Mẫu biểu, gồm nhiều field. Phải **Publish** mới dùng được |
| **Field** | Ô nhập liệu trong form (text, date, select…) |
| **Page** | Màn hình ghép nhiều form theo layout grid 12 cột |
| **Submission** | Dữ liệu người dùng đã điền và submit |

**Hai nhóm endpoint:**

| Nhóm | Prefix | Mục đích |
|------|--------|----------|
| Admin | `/forms/admin/...` | Tạo / cấu hình (cần phân quyền) |
| Public | `/forms/...` | Frontend đọc schema và submit |

---

## 2. Quy tắc chung

### Định dạng response

Tất cả response đều bọc trong `ApiResponse<T>`:

```jsonc
// Thành công
{
  "success": true,
  "data": { ... }
}

// Lỗi
{
  "success": false,
  "error": {
    "code": "NotFound",
    "message": "Module 'tiep-nhan' không tồn tại"
  }
}
```

### Quy tắc đặt `code` / `key`

- Chỉ gồm chữ **thường**, **số**, và **dấu gạch ngang** `-` (với module/form/page code)
- Field key chỉ gồm chữ thường, số, và **dấu gạch dưới** `_`
- Không dấu, không khoảng trắng
- Ví dụ hợp lệ: `tiep-nhan`, `phieu-tiep-nhan`, `ho_ten`, `ngay_sinh`

### Vòng đời

```
Form:   Draft → Published → Archived
Page:   Draft → Published → Archived
Module: Active / Inactive
```

- **Draft**: chỉ admin thấy, chưa dùng được
- **Published**: frontend đọc được, không sửa field nữa
- **Archived**: ngừng sử dụng

---

## 3. Module API

Module là đơn vị tổ chức cao nhất. Một hệ thống thường có 5–10 module tương ứng các phòng/chức năng.

---

### 3.1 Tạo module

```
POST /forms/admin/modules
```

**Request body:**

```json
{
  "code": "tiep-nhan",
  "name": "Tiếp nhận bệnh nhân",
  "description": "Quản lý phiếu tiếp nhận đầu vào"
}
```

| Field | Bắt buộc | Kiểu | Mô tả |
|-------|----------|------|-------|
| `code` | ✅ | string (≤50) | Slug duy nhất, chữ thường + số + gạch ngang |
| `name` | ✅ | string (≤200) | Tên hiển thị |
| `description` | ❌ | string (≤500) | Mô tả |

**Response 201:**

```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "code": "tiep-nhan",
    "name": "Tiếp nhận bệnh nhân",
    "description": "Quản lý phiếu tiếp nhận đầu vào",
    "status": "Active",
    "formCount": 0,
    "createdAtUtc": "2026-06-01T08:00:00Z"
  }
}
```

**Lỗi thường gặp:**

| HTTP | Code | Nguyên nhân |
|------|------|-------------|
| 400 | `Conflict` | `code` đã tồn tại |
| 400 | `Validation` | `code` chứa ký tự không hợp lệ |

---

### 3.2 Lấy danh sách module

```
GET /forms/modules
```

Không cần tham số. Trả tất cả module kèm số lượng form.

**Response 200:**

```json
{
  "success": true,
  "data": [
    {
      "id": "3fa85f64-...",
      "code": "tiep-nhan",
      "name": "Tiếp nhận bệnh nhân",
      "status": "Active",
      "formCount": 3,
      "createdAtUtc": "2026-06-01T08:00:00Z"
    }
  ]
}
```

---

## 4. Form API

Form là mẫu biểu chứa các field. Một module có thể có nhiều form.

---

### 4.1 Tạo form

```
POST /forms/admin/modules/{moduleCode}/forms
```

**URL param:** `moduleCode` — code của module đã tạo.

**Request body:**

```json
{
  "key": "phieu-tiep-nhan",
  "name": "Phiếu Tiếp Nhận",
  "description": "Thu thập thông tin bệnh nhân lúc nhập viện",
  "submitButtonLabel": "Lưu phiếu",
  "successMessage": "Đã lưu phiếu tiếp nhận thành công",
  "allowMultipleSubmissions": true
}
```

| Field | Bắt buộc | Mặc định | Mô tả |
|-------|----------|----------|-------|
| `key` | ✅ | — | Slug duy nhất trong module, chữ thường + số + gạch ngang |
| `name` | ✅ | — | Tên form |
| `description` | ❌ | null | Mô tả |
| `submitButtonLabel` | ❌ | `"Gửi"` | Nhãn nút submit trên frontend |
| `successMessage` | ❌ | `"Đã gửi form thành công"` | Thông báo sau submit |
| `allowMultipleSubmissions` | ❌ | `true` | Cho phép submit nhiều lần |

**Response 201:**

```json
{
  "success": true,
  "data": {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "moduleCode": "tiep-nhan",
    "key": "phieu-tiep-nhan",
    "name": "Phiếu Tiếp Nhận",
    "status": "Draft",
    "version": 1,
    "fieldCount": 0,
    "createdAtUtc": "2026-06-01T08:05:00Z"
  }
}
```

> **Quan trọng:** Lưu lại `id` để dùng trong các bước thêm field, publish, archive.

---

### 4.2 Publish form

Form phải được publish thì frontend mới đọc được schema.

```
POST /forms/admin/forms/{formId}/publish
```

**Điều kiện:** Form phải có ít nhất 1 field. Form đang ở trạng thái `Draft` hoặc `Published`.

**Response 200:**

```json
{ "success": true }
```

**Lỗi:**

| HTTP | Nguyên nhân |
|------|-------------|
| 400 | Form chưa có field nào |
| 400 | Form đang là `Archived` |
| 404 | `formId` không tồn tại |

---

### 4.3 Archive form

Ngừng sử dụng form (không thể publish lại).

```
POST /forms/admin/forms/{formId}/archive
```

**Response 200:**

```json
{ "success": true }
```

---

### 4.4 Lấy danh sách form của module

```
GET /forms/{moduleCode}
```

**Response 200:**

```json
{
  "success": true,
  "data": [
    {
      "id": "7c9e6679-...",
      "moduleCode": "tiep-nhan",
      "key": "phieu-tiep-nhan",
      "name": "Phiếu Tiếp Nhận",
      "status": "Published",
      "version": 2,
      "fieldCount": 8,
      "createdAtUtc": "2026-06-01T08:05:00Z"
    }
  ]
}
```

---

## 5. Field API

Field là các ô nhập liệu trong form. Thêm field khi form đang ở trạng thái **Draft**.

---

### 5.1 Thêm field vào form

```
POST /forms/admin/forms/{formId}/fields
```

**Request body đầy đủ:**

```json
{
  "key": "ho_ten",
  "label": "Họ và tên",
  "fieldType": "Text",
  "order": 1,
  "required": true,
  "width": "Half",
  "placeholder": "Nhập họ tên đầy đủ",
  "helpText": "Ghi theo CMND/CCCD",
  "options": null,
  "validationRules": null,
  "conditionalLogic": null
}
```

| Field | Bắt buộc | Mô tả |
|-------|----------|-------|
| `key` | ✅ | Slug duy nhất trong form, chữ thường + số + gạch dưới |
| `label` | ✅ | Nhãn hiển thị trên UI |
| `fieldType` | ✅ | Xem bảng loại field bên dưới |
| `order` | ✅ | Thứ tự hiển thị (bắt đầu từ 1) |
| `required` | ✅ | Bắt buộc điền hay không |
| `width` | ✅ | `Full` / `Half` / `Third` |
| `placeholder` | ❌ | Placeholder text |
| `helpText` | ❌ | Hướng dẫn nhỏ bên dưới field |
| `options` | ❌ | Dùng cho Select/MultiSelect/Radio |
| `validationRules` | ❌ | Validation bổ sung |
| `conditionalLogic` | ❌ | Hiển thị có điều kiện |

---

### 5.2 Bảng các loại field (`fieldType`)

| Giá trị | UI Component | Ghi chú |
|---------|-------------|---------|
| `Text` | Input text | Ô nhập 1 dòng |
| `Textarea` | Textarea | Ô nhập nhiều dòng |
| `Number` | Input number | Chỉ nhập số |
| `Date` | Date picker | Định dạng YYYY-MM-DD |
| `DateTime` | DateTime picker | Định dạng ISO 8601 |
| `Select` | Dropdown | Chọn 1 trong nhiều, cần `options` |
| `MultiSelect` | Multi-dropdown | Chọn nhiều, cần `options` |
| `Radio` | Radio buttons | Chọn 1, cần `options` |
| `Checkbox` | Checkbox | true/false |
| `File` | File upload | Upload file |
| `Signature` | Ký tên | Canvas ký tên |
| `Section` | Tiêu đề | Phân chia nhóm field, không nhập liệu |

---

### 5.3 Bảng width

| Giá trị | Số cột (grid 12) | Tỉ lệ |
|---------|-----------------|-------|
| `Full` | 12 | 100% |
| `Half` | 6 | 50% |
| `Third` | 4 | 33% |

---

### 5.4 Field có Options (Select / MultiSelect / Radio)

```json
{
  "key": "gioi_tinh",
  "label": "Giới tính",
  "fieldType": "Select",
  "order": 3,
  "required": true,
  "width": "Half",
  "options": [
    { "label": "Nam",   "value": "male"   },
    { "label": "Nữ",   "value": "female" },
    { "label": "Khác", "value": "other"  }
  ]
}
```

---

### 5.5 Field có ValidationRules

```json
{
  "key": "so_dien_thoai",
  "label": "Số điện thoại",
  "fieldType": "Text",
  "order": 5,
  "required": true,
  "width": "Half",
  "validationRules": [
    {
      "type": "pattern",
      "value": "^(0|\\+84)[0-9]{9}$",
      "errorMessage": "Số điện thoại không hợp lệ"
    },
    {
      "type": "minLength",
      "value": "10",
      "errorMessage": "Tối thiểu 10 ký tự"
    }
  ]
}
```

**Các `type` validation hợp lệ:**

| Type | Value | Ý nghĩa |
|------|-------|---------|
| `required` | `"true"` | Bắt buộc |
| `minLength` | số | Độ dài tối thiểu |
| `maxLength` | số | Độ dài tối đa |
| `min` | số | Giá trị số tối thiểu |
| `max` | số | Giá trị số tối đa |
| `pattern` | regex | Regex kiểm tra format |

---

### 5.6 Field có ConditionalLogic (hiển thị theo điều kiện)

Field này chỉ hiện khi field khác có giá trị nhất định.

```json
{
  "key": "ten_nguoi_bao_lanh",
  "label": "Tên người bảo lãnh",
  "fieldType": "Text",
  "order": 7,
  "required": false,
  "width": "Half",
  "conditionalLogic": {
    "sourceFieldKey": "co_nguoi_bao_lanh",
    "operator": "Equals",
    "value": "yes",
    "action": "Show"
  }
}
```

| Field | Giá trị hợp lệ | Ý nghĩa |
|-------|---------------|---------|
| `sourceFieldKey` | key của field khác | Field nào kích hoạt điều kiện |
| `operator` | `Equals` / `NotEquals` / `Contains` | Phép so sánh |
| `value` | string | Giá trị so sánh |
| `action` | `Show` / `Hide` | Hành động khi điều kiện đúng |

**Ví dụ đọc:** *"Hiện field `ten_nguoi_bao_lanh` khi field `co_nguoi_bao_lanh` bằng `yes`"*

---

### 5.7 Field Section (phân chia nhóm)

```json
{
  "key": "section_thong_tin_ca_nhan",
  "label": "Thông tin cá nhân",
  "fieldType": "Section",
  "order": 0,
  "required": false,
  "width": "Full"
}
```

Field `Section` là tiêu đề phân đoạn, không nhập liệu. Frontend render thành heading.

**Response thêm field thành công:**

```json
{
  "success": true,
  "data": {
    "id": "d290f1ee-...",
    "key": "ho_ten",
    "label": "Họ và tên",
    "type": "text",
    "order": 1,
    "required": true,
    "width": "half",
    "placeholder": "Nhập họ tên đầy đủ",
    "helpText": "Ghi theo CMND/CCCD",
    "options": null,
    "validationRules": null,
    "conditionalLogic": null
  }
}
```

---

## 6. Form Schema (BDUI)

Endpoint chính để frontend đọc và render form động. Chỉ hoạt động với form đã **Published**.

---

### 6.1 Lấy schema của một form

```
GET /forms/{moduleCode}/{formKey}/schema
```

**Response 200:**

```json
{
  "success": true,
  "data": {
    "id": "7c9e6679-...",
    "moduleCode": "tiep-nhan",
    "formKey": "phieu-tiep-nhan",
    "name": "Phiếu Tiếp Nhận",
    "description": "Thu thập thông tin bệnh nhân lúc nhập viện",
    "version": 1,
    "fields": [
      {
        "id": "d290f1ee-...",
        "key": "section_thong_tin_ca_nhan",
        "label": "Thông tin cá nhân",
        "type": "section",
        "order": 0,
        "required": false,
        "width": "full",
        "placeholder": null,
        "helpText": null,
        "options": null,
        "validationRules": null,
        "conditionalLogic": null
      },
      {
        "id": "a1b2c3d4-...",
        "key": "ho_ten",
        "label": "Họ và tên",
        "type": "text",
        "order": 1,
        "required": true,
        "width": "half",
        "placeholder": "Nhập họ tên đầy đủ",
        "helpText": "Ghi theo CMND/CCCD",
        "options": null,
        "validationRules": null,
        "conditionalLogic": null
      },
      {
        "id": "e5f6g7h8-...",
        "key": "gioi_tinh",
        "label": "Giới tính",
        "type": "select",
        "order": 2,
        "required": true,
        "width": "half",
        "options": [
          { "label": "Nam",   "value": "male"   },
          { "label": "Nữ",   "value": "female" },
          { "label": "Khác", "value": "other"  }
        ],
        "validationRules": null,
        "conditionalLogic": null
      }
    ],
    "settings": {
      "submitButtonLabel": "Lưu phiếu",
      "successMessage": "Đã lưu phiếu tiếp nhận thành công",
      "allowMultipleSubmissions": true
    }
  }
}
```

> `fields` luôn được sắp xếp theo `order` tăng dần.  
> `type` và `width` trả về **chữ thường** (`text`, `select`, `half`…).

**Lỗi:**

| HTTP | Nguyên nhân |
|------|-------------|
| 404 | Module hoặc form không tồn tại |
| 400 | Form chưa được publish |

---

## 7. Submission API

---

### 7.1 Submit form

```
POST /forms/{moduleCode}/{formKey}/submit
```

**Request body:**

```json
{
  "answers": [
    { "fieldKey": "ho_ten",    "value": "Nguyễn Văn An" },
    { "fieldKey": "gioi_tinh", "value": "male"           },
    { "fieldKey": "ngay_sinh", "value": "1990-05-15"     },
    { "fieldKey": "so_dien_thoai", "value": "0912345678" }
  ]
}
```

- `fieldKey` phải khớp với `key` của field trong schema.
- `value` luôn là **string** (kể cả số, ngày, boolean).
- Không cần gửi field `Section` (không có giá trị).

**Response 200:**

```json
{
  "success": true,
  "data": {
    "submissionId": "f47ac10b-58cc-4372-a567-0e02b2c3d479"
  }
}
```

**Lỗi:**

| HTTP | Nguyên nhân |
|------|-------------|
| 400 | Form chưa publish |
| 404 | Module hoặc form không tồn tại |

---

### 7.2 Xem danh sách submission (Admin)

```
GET /forms/admin/forms/{formId}/submissions?page=1&pageSize=20
```

**Query params:**

| Param | Mặc định | Mô tả |
|-------|----------|-------|
| `page` | 1 | Trang hiện tại |
| `pageSize` | 20 | Số bản ghi mỗi trang |

**Response 200:**

```json
{
  "success": true,
  "data": [
    {
      "id": "f47ac10b-...",
      "moduleCode": "tiep-nhan",
      "formKey": "phieu-tiep-nhan",
      "formVersion": 1,
      "submittedBy": null,
      "status": "Submitted",
      "submittedAt": "2026-06-01T09:30:00Z",
      "answers": {
        "ho_ten": "Nguyễn Văn An",
        "gioi_tinh": "male",
        "ngay_sinh": "1990-05-15"
      }
    }
  ]
}
```

---

## 8. Page API (multi-form layout)

Page cho phép ghép nhiều form vào một màn hình theo grid 12 cột. Frontend chỉ cần **1 request** để lấy toàn bộ layout + schema của tất cả form.

---

### 8.1 Tạo page

```
POST /forms/admin/modules/{moduleCode}/pages
```

**Request body:**

```json
{
  "code": "man-hinh-tiep-nhan",
  "title": "Màn hình Tiếp nhận",
  "description": "Phiếu tiếp nhận và bảo hiểm trên cùng một màn hình"
}
```

| Field | Bắt buộc | Mô tả |
|-------|----------|-------|
| `code` | ✅ | Slug duy nhất trong module |
| `title` | ✅ | Tiêu đề màn hình |
| `description` | ❌ | Mô tả |

**Response 201:**

```json
{
  "success": true,
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "moduleCode": "tiep-nhan",
    "code": "man-hinh-tiep-nhan",
    "title": "Màn hình Tiếp nhận",
    "status": "Draft",
    "createdAtUtc": "2026-06-01T08:00:00Z"
  }
}
```

> Lưu `id` để dùng cho bước cài layout và publish.

---

### 8.2 Cài đặt layout

```
PUT /forms/admin/pages/{pageId}/layout
```

Layout dùng hệ grid **12 cột**. Mỗi `row` là một hàng, mỗi `component` trong hàng có `span` (số cột chiếm). Tổng `span` trong một `row` nên ≤ 12.

**Request body:**

```json
{
  "rows": [
    {
      "components": [
        {
          "type": "FormSection",
          "span": 8,
          "formKey": "phieu-tiep-nhan",
          "title": "Thông tin bệnh nhân"
        },
        {
          "type": "FormSection",
          "span": 4,
          "formKey": "phieu-bao-hiem"
        }
      ]
    },
    {
      "components": [
        {
          "type": "TextBlock",
          "span": 12,
          "content": "⚠️ Kiểm tra kỹ thông tin trước khi lưu.",
          "align": "center"
        }
      ]
    },
    {
      "components": [
        {
          "type": "Divider",
          "span": 12,
          "label": "Xác nhận của điều dưỡng"
        }
      ]
    }
  ]
}
```

**Các loại component:**

#### `FormSection` — nhúng một form vào vị trí này

| Field | Bắt buộc | Mô tả |
|-------|----------|-------|
| `type` | ✅ | `"FormSection"` |
| `span` | ❌ | Số cột (1–12), mặc định null = full |
| `formKey` | ✅ | `key` của FormTemplate cùng module, phải đã Published |
| `title` | ❌ | Override tiêu đề form (nếu muốn đổi khác tên form gốc) |

#### `TextBlock` — khối văn bản / hướng dẫn

| Field | Bắt buộc | Mô tả |
|-------|----------|-------|
| `type` | ✅ | `"TextBlock"` |
| `span` | ❌ | Số cột |
| `content` | ✅ | Nội dung văn bản |
| `align` | ❌ | `"left"` (mặc định) / `"center"` / `"right"` |

#### `Divider` — đường phân cách

| Field | Bắt buộc | Mô tả |
|-------|----------|-------|
| `type` | ✅ | `"Divider"` |
| `span` | ❌ | Số cột |
| `label` | ❌ | Text trên đường kẻ |

**Response 200:**

```json
{ "success": true }
```

> Có thể gọi lại nhiều lần để cập nhật layout. Page phải ở trạng thái `Draft` hoặc `Published` (chưa `Archived`).

---

### 8.3 Publish page

```
POST /forms/admin/pages/{pageId}/publish
```

**Response 200:**

```json
{ "success": true }
```

---

### 8.4 Lấy schema page (BDUI — public)

Đây là endpoint frontend gọi để render toàn bộ màn hình. Trả về layout + schema đầy đủ của mỗi form trong một lần gọi.

```
GET /forms/pages/{moduleCode}/{pageCode}
```

**Response 200:**

```json
{
  "success": true,
  "data": {
    "id": "550e8400-...",
    "moduleCode": "tiep-nhan",
    "code": "man-hinh-tiep-nhan",
    "title": "Màn hình Tiếp nhận",
    "description": "Phiếu tiếp nhận và bảo hiểm trên cùng một màn hình",
    "rows": [
      {
        "components": [
          {
            "type": "FormSection",
            "span": 8,
            "formKey": "phieu-tiep-nhan",
            "title": "Thông tin bệnh nhân",
            "schema": {
              "id": "7c9e6679-...",
              "moduleCode": "tiep-nhan",
              "formKey": "phieu-tiep-nhan",
              "name": "Phiếu Tiếp Nhận",
              "version": 1,
              "fields": [ ... ],
              "settings": {
                "submitButtonLabel": "Lưu phiếu",
                "successMessage": "Đã lưu phiếu thành công",
                "allowMultipleSubmissions": true
              }
            }
          },
          {
            "type": "FormSection",
            "span": 4,
            "formKey": "phieu-bao-hiem",
            "title": null,
            "schema": { ... }
          }
        ]
      },
      {
        "components": [
          {
            "type": "TextBlock",
            "span": 12,
            "content": "⚠️ Kiểm tra kỹ thông tin trước khi lưu.",
            "align": "center"
          }
        ]
      }
    ],
    "generatedAt": "2026-06-01T10:00:00Z"
  }
}
```

**Lỗi:**

| HTTP | Nguyên nhân |
|------|-------------|
| 404 | Module hoặc page không tồn tại |
| 400 | Page chưa được publish |

---

### 8.5 Lấy danh sách page của module

```
GET /forms/pages/{moduleCode}
```

**Response 200:**

```json
{
  "success": true,
  "data": [
    {
      "id": "550e8400-...",
      "moduleCode": "tiep-nhan",
      "code": "man-hinh-tiep-nhan",
      "title": "Màn hình Tiếp nhận",
      "status": "Published",
      "createdAtUtc": "2026-06-01T08:00:00Z"
    }
  ]
}
```

---

## 9. Luồng hoàn chỉnh — ví dụ thực tế

### Bài toán: Màn hình "Tiếp nhận bệnh nhân" gồm 2 form

```
[Thông tin BN - 8 cột] [Bảo hiểm - 4 cột]
[Ghi chú: Kiểm tra trước khi lưu - 12 cột]
```

---

**Bước 1 — Tạo module**

```http
POST /forms/admin/modules
{ "code": "tiep-nhan", "name": "Tiếp nhận bệnh nhân" }
```

---

**Bước 2 — Tạo form "Phiếu tiếp nhận"**

```http
POST /forms/admin/modules/tiep-nhan/forms
{
  "key": "phieu-tiep-nhan",
  "name": "Phiếu Tiếp Nhận",
  "submitButtonLabel": "Lưu phiếu"
}
```
→ lưu `id` = `{formA-id}`

---

**Bước 3 — Thêm field vào form A**

```http
POST /forms/admin/forms/{formA-id}/fields
{ "key": "ho_ten",   "label": "Họ và tên",  "fieldType": "Text",   "order": 1, "required": true,  "width": "Half" }

POST /forms/admin/forms/{formA-id}/fields
{ "key": "ngay_sinh","label": "Ngày sinh",  "fieldType": "Date",   "order": 2, "required": true,  "width": "Half" }

POST /forms/admin/forms/{formA-id}/fields
{ "key": "gioi_tinh","label": "Giới tính",  "fieldType": "Select", "order": 3, "required": true,  "width": "Half",
  "options": [{"label":"Nam","value":"male"},{"label":"Nữ","value":"female"}] }

POST /forms/admin/forms/{formA-id}/fields
{ "key": "so_dien_thoai","label": "Số ĐT", "fieldType": "Text",   "order": 4, "required": true,  "width": "Half" }
```

---

**Bước 4 — Publish form A**

```http
POST /forms/admin/forms/{formA-id}/publish
```

---

**Bước 5 — Tạo + thêm field + publish form "Phiếu bảo hiểm"**

```http
POST /forms/admin/modules/tiep-nhan/forms
{ "key": "phieu-bao-hiem", "name": "Phiếu Bảo Hiểm" }
→ {formB-id}

POST /forms/admin/forms/{formB-id}/fields
{ "key": "ma_the_bhyt", "label": "Mã thẻ BHYT", "fieldType": "Text", "order": 1, "required": true, "width": "Full" }

POST /forms/admin/forms/{formB-id}/fields
{ "key": "noi_dang_ky", "label": "Nơi đăng ký KCB", "fieldType": "Text", "order": 2, "required": true, "width": "Full" }

POST /forms/admin/forms/{formB-id}/publish
```

---

**Bước 6 — Tạo Page**

```http
POST /forms/admin/modules/tiep-nhan/pages
{ "code": "man-hinh-tiep-nhan", "title": "Màn hình Tiếp nhận" }
→ {pageId}
```

---

**Bước 7 — Cài layout**

```http
PUT /forms/admin/pages/{pageId}/layout
{
  "rows": [
    { "components": [
        { "type": "FormSection", "span": 8, "formKey": "phieu-tiep-nhan", "title": "Thông tin bệnh nhân" },
        { "type": "FormSection", "span": 4, "formKey": "phieu-bao-hiem"  }
    ]},
    { "components": [
        { "type": "TextBlock", "span": 12, "content": "Kiểm tra kỹ trước khi lưu", "align": "center" }
    ]}
  ]
}
```

---

**Bước 8 — Publish Page**

```http
POST /forms/admin/pages/{pageId}/publish
```

---

**Bước 9 — Frontend gọi 1 request**

```http
GET /forms/pages/tiep-nhan/man-hinh-tiep-nhan
```

→ Nhận toàn bộ layout + schema → render màn hình → sau đó submit từng form riêng:

```http
POST /forms/tiep-nhan/phieu-tiep-nhan/submit
{ "answers": [{"fieldKey":"ho_ten","value":"Nguyễn Văn A"}, ...] }

POST /forms/tiep-nhan/phieu-bao-hiem/submit
{ "answers": [{"fieldKey":"ma_the_bhyt","value":"DN4050012345"}, ...] }
```

---

## 10. Bảng tham chiếu nhanh

### Tất cả endpoints

| Method | URL | Mô tả | Auth |
|--------|-----|-------|------|
| `POST` | `/forms/admin/modules` | Tạo module | Admin |
| `GET` | `/forms/modules` | Danh sách module | Public |
| `POST` | `/forms/admin/modules/{code}/forms` | Tạo form | Admin |
| `GET` | `/forms/{moduleCode}` | Danh sách form của module | Public |
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
