# 33 — Screen Designer (SDUI + Drag-and-Drop)

## Tổng quan

**Screen Designer** là tính năng trong `DynamicFormService` cho phép admin thiết kế màn hình động (SDUI — Server-Driven UI) bằng cách kéo-thả widget lên một canvas grid. Frontend nhận layout từ server và render giao diện mà không cần biết cấu trúc trước.

Tính năng này **thay thế hoàn toàn** hệ thống `FormPage` cũ (blob JSON không có cấu trúc).

---

## Kiến trúc dữ liệu

```
FormModule (code, name)
  └── FormScreen (code, title, status, sortOrder)
        └── FormScreenTab (label, slug, sortOrder, isDefault)
              └── FormScreenWidget (widgetKey, widgetType, gridX, gridY, gridW, gridH,
                                    configJson, referenceId?)
```

### FormScreen

| Field | Mô tả |
|---|---|
| `code` | Slug định danh, unique trong module |
| `status` | `Draft` → `Published` → `Archived` |
| `sortOrder` | Thứ tự hiển thị trong sidebar |

### FormScreenTab

| Field | Mô tả |
|---|---|
| `slug` | Slug định danh, unique trong screen |
| `isDefault` | Tab được chọn mặc định khi mở screen |
| `sortOrder` | Thứ tự hiển thị |

### FormScreenWidget

| Field | Mô tả |
|---|---|
| `widgetKey` | Định danh widget trong tab (unique per tab) |
| `widgetType` | Loại widget (xem bảng dưới) |
| `gridX`, `gridY` | Vị trí trên grid (cột, hàng) — dùng cho react-grid-layout |
| `gridW`, `gridH` | Kích thước widget (default 6×4) |
| `configJson` | JSON cấu hình hiển thị (màu, nội dung, URL ảnh...) |
| `referenceId` | Trỏ tới `FormTemplate.Id` nếu `widgetType = FormSection` |

---

## Widget Types

| widgetType | Mô tả | Default W×H |
|---|---|---|
| `FormSection` | Nhúng một FormTemplate (form động) vào vị trí này | 6×8 |
| `TextBlock` | Tiêu đề hoặc đoạn văn bản hướng dẫn (markdown) | 6×2 |
| `Divider` | Đường ngang phân cách các section | 12×1 |
| `ImageBlock` | Hình ảnh tĩnh (URL trong configJson) | 4×4 |
| `ConditionalSection` | Container ẩn/hiện theo giá trị của một form field | 6×6 |

---

## API Endpoints

### Admin — Quản lý Screen

| Method | Endpoint | Mô tả |
|---|---|---|
| `GET` | `/forms/admin/screens/{moduleCode}` | Danh sách screens của module |
| `POST` | `/forms/admin/screens` | Tạo screen mới |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}` | Cập nhật screen |
| `DELETE` | `/forms/admin/screens/{moduleCode}/{screenCode}` | Xóa screen (cascade tabs + widgets) |
| `POST` | `/forms/admin/screens/{moduleCode}/{screenCode}/publish` | Publish screen |

### Admin — Quản lý Tab

| Method | Endpoint | Mô tả |
|---|---|---|
| `POST` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs` | Thêm tab |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | Cập nhật tab |
| `DELETE` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | Xóa tab (cascade widgets) |

### Admin — Widget Canvas (Drag-and-Drop Save)

| Method | Endpoint | Mô tả |
|---|---|---|
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}/widgets` | **Lưu toàn bộ canvas** — full replacement |
| `GET` | `/forms/admin/widget-catalog` | Danh sách widget types được hỗ trợ |

### Public — SDUI Rendering

| Method | Endpoint | Mô tả |
|---|---|---|
| `GET` | `/forms/screens/{moduleCode}` | Danh sách screens (đã published) |
| `GET` | `/forms/screens/{moduleCode}/{screenCode}/layout` | **SDUI endpoint chính** — toàn bộ layout |

---

## Request / Response mẫu

### Tạo Screen

```
POST /forms/admin/screens
{
  "moduleCode": "hr",
  "code": "onboarding",
  "title": "Onboarding nhân viên mới",
  "description": "Màn hình tiếp nhận nhân viên",
  "sortOrder": 0
}

→ 201
{
  "id": "uuid",
  "code": "onboarding",
  "title": "Onboarding nhân viên mới",
  "status": "Draft",
  ...
}
```

### Thêm Tab

```
POST /forms/admin/screens/hr/onboarding/tabs
{
  "label": "Thông tin cá nhân",
  "slug": "personal-info",
  "sortOrder": 0,
  "isDefault": true
}
```

### Lưu Widget Canvas (drag-and-drop save)

```
PUT /forms/admin/screens/hr/onboarding/tabs/{tabId}/widgets
[
  {
    "widgetKey": "personal-form",
    "widgetType": "FormSection",
    "gridX": 0, "gridY": 0, "gridW": 8, "gridH": 10,
    "configJson": "{}",
    "referenceId": "uuid-of-form-template"
  },
  {
    "widgetKey": "welcome-text",
    "widgetType": "TextBlock",
    "gridX": 8, "gridY": 0, "gridW": 4, "gridH": 3,
    "configJson": "{\"content\": \"Chào mừng bạn đến với công ty!\", \"align\": \"center\"}",
    "referenceId": null
  }
]

→ 200
{ "saved": 2 }
```

### SDUI Layout (frontend gọi khi render màn hình)

```
GET /forms/screens/hr/onboarding/layout

→ 200
{
  "id": "uuid",
  "moduleCode": "hr",
  "code": "onboarding",
  "title": "Onboarding nhân viên mới",
  "tabs": [
    {
      "id": "uuid",
      "label": "Thông tin cá nhân",
      "slug": "personal-info",
      "isDefault": true,
      "widgets": [
        {
          "widgetKey": "personal-form",
          "widgetType": "FormSection",
          "gridX": 0, "gridY": 0, "gridW": 8, "gridH": 10,
          "config": null,
          "referenceId": "uuid",
          "formSchema": {
            "formKey": "personal-info-form",
            "fields": [...],
            "settings": {...}
          }
        },
        {
          "widgetKey": "welcome-text",
          "widgetType": "TextBlock",
          "gridX": 8, "gridY": 0, "gridW": 4, "gridH": 3,
          "config": { "content": "Chào mừng...", "align": "center" },
          "referenceId": null,
          "formSchema": null
        }
      ]
    }
  ],
  "generatedAt": "2026-06-03T..."
}
```

---

## Luồng hoạt động

### Luồng 1: Admin thiết kế screen

```
Admin mở Screen Designer
  │
  ├─ GET /forms/admin/screens/{moduleCode}       — load danh sách screens
  ├─ GET /forms/admin/widget-catalog             — load widget palette
  │
  ├─ POST .../screens                            — tạo screen mới
  ├─ POST .../screens/{code}/tabs                — thêm tab
  │
  ├─ [Kéo thả widget vào canvas, chỉnh vị trí x/y/w/h]
  │
  ├─ PUT .../tabs/{tabId}/widgets                — lưu toàn bộ canvas (full replace)
  │
  └─ POST .../screens/{code}/publish             — publish để user xem được
```

### Luồng 2: Frontend render màn hình SDUI

```
User mở màn hình
  │
  ├─ GET /forms/screens/{moduleCode}/{code}/layout
  │    → Nhận layout đầy đủ (tabs + widgets + formSchema nếu FormSection)
  │
  └─ [Frontend render từng widget theo widgetType và gridX/Y/W/H]
       FormSection      → render form dynamic với fields từ formSchema
       TextBlock        → render markdown content
       Divider          → render hr
       ImageBlock       → render img với src từ config.url
       ConditionalSection → render/hide theo config.condition
```

---

## Vị trí code

| Layer | File |
|---|---|
| Domain Entities | `DynamicFormService.Domain/Entities/FormScreen.cs` |
| Domain Entities | `DynamicFormService.Domain/Entities/FormScreenTab.cs` |
| Domain Entities | `DynamicFormService.Domain/Entities/FormScreenWidget.cs` |
| Domain Enum | `DynamicFormService.Domain/Enums/WidgetType.cs` |
| Repository Interface | `DynamicFormService.Domain/Repositories/IFormScreenRepository.cs` |
| Domain Event | `DynamicFormService.Domain/Events/FormScreenPublishedDomainEvent.cs` |
| SDUI Query | `DynamicFormService.Application/Features/Screens/GetScreenLayout/GetScreenLayoutQuery.cs` |
| Drag-drop Save | `DynamicFormService.Application/Features/Tabs/SaveTabWidgets/SaveTabWidgetsCommand.cs` |
| Widget Catalog | `DynamicFormService.Application/Features/WidgetCatalog/GetWidgetCatalog/GetWidgetCatalogQuery.cs` |
| EF Configuration | `DynamicFormService.Infrastructure/Persistence/Configurations/FormScreen*.cs` |
| Repository Impl | `DynamicFormService.Infrastructure/Persistence/FormScreenRepository.cs` |
| Admin API | `DynamicFormService.API/Controllers/AdminScreensController.cs` |
| Public SDUI API | `DynamicFormService.API/Controllers/ScreensController.cs` |
| Migration | `DynamicFormService.Infrastructure/Persistence/Migrations/20260603021333_AddScreenDesigner.cs` |

---

## Thay đổi so với FormPage cũ

| FormPage (cũ) | Screen Designer (mới) |
|---|---|
| Layout là blob JSON opaque | Layout là entity có cấu trúc (Tab → Widget) |
| Không hỗ trợ tab | Có tabs với `isDefault` |
| Không có grid position | `gridX`, `gridY`, `gridW`, `gridH` cho react-grid-layout |
| Chỉ có 3 loại component | 5 widget types, có thể mở rộng |
| Không cascade xóa structured | Cascade xóa Tab → Widget |
| 1 endpoint lưu layout (PUT) | 1 endpoint save canvas: `PUT .../tabs/{id}/widgets` |
