# 29 — DynamicFormService

## Tổng quan

DynamicFormService quản lý hai nhóm chức năng độc lập:

| Nhóm | Mục đích |
|------|----------|
| **Dynamic Forms (BDUI)** | Admin định nghĩa form y tế tại runtime; frontend nhận schema và render động |
| **Screen Designer (SDUI)** | Admin kéo-thả widget lên canvas để thiết kế màn hình; frontend nhận layout từ server |

Database: **PostgreSQL** (`DynamicFormDb`, host `postgres-df:5432`).

---

## Domain Model

```
FormModule (code, name, status)
  ├── FormTemplate (key, name, status, version)
  │     └── FormField (key, label, type, order, required, width, options, validationRules, conditionalLogic)
  ├── FormSubmission (formKey, answers, submittedBy)
  └── FormScreen (code, title, status, sortOrder)
        └── FormScreenTab (label, slug, isDefault, sortOrder)
              └── FormScreenWidget (widgetKey, widgetType, gridX, gridY, gridW, gridH, configJson, referenceId?)

WidgetCatalog (chartType, category, label, icon, requiredColumns, optionalColumns, compatibleWith)
```

### Enums

| Enum | Giá trị |
|------|---------|
| `ModuleStatus` | `Active`, `Inactive` |
| `FormStatus` | `Draft`, `Published`, `Archived` (dùng chung cho Form và Screen) |
| `FieldType` | `Text`, `Textarea`, `Number`, `Date`, `DateTime`, `Select`, `Multiselect`, `Radio`, `Checkbox`, `File`, `Signature`, `Section` |
| `FieldWidth` | `Full`, `Half`, `Third` |
| `SubmissionStatus` | `Submitted`, `Reviewed` |
| `WidgetType` | `FormSection`, `TextBlock`, `Divider`, `ImageBlock`, `ConditionalSection` |

### Lifecycle

```
FormTemplate / FormScreen:
  Draft ──publish()──► Published ──archive()──► Archived
```

- **Draft**: admin thấy, có thể sửa
- **Published**: public thấy, không được sửa field/widget
- **Archived**: không nhận thêm submission / ẩn khỏi danh sách

---

## API Endpoints

### Public — Module & Form

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/forms/modules` | Danh sách modules active với `formCount` và `screenCount` thực tế |
| `GET` | `/forms/{moduleCode}` | Danh sách FormTemplate trong module |
| `GET` | `/forms/{moduleCode}/pages` | **Danh sách published screens (pages) trong module** ⭐ |
| `GET` | `/forms/{moduleCode}/{formKey}/schema` | BDUI schema — frontend dùng để render form |
| `POST` | `/forms/{moduleCode}/{formKey}/submit` | Submit form |
| `GET` | `/forms/health` | Health check |

### Public — Screen SDUI

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/forms/screens/{moduleCode}` | Danh sách screens (all status) |
| `GET` | `/forms/screens/{moduleCode}/{screenCode}/layout` | **SDUI layout** — tabs + widgets + formSchema |

### Admin — Module & Form

| Method | Route | Mô tả |
|--------|-------|-------|
| `POST` | `/forms/admin/modules` | Tạo module |
| `POST` | `/forms/admin/modules/{moduleCode}/forms` | Tạo form |
| `POST` | `/forms/admin/forms/{id}/fields` | Thêm field |
| `POST` | `/forms/admin/forms/{id}/publish` | Publish form |
| `POST` | `/forms/admin/forms/{id}/archive` | Archive form |
| `GET` | `/forms/admin/forms/{id}/submissions` | Xem submissions |

### Admin — Screen Designer (URL "screens")

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/forms/admin/widget-catalog` | Danh mục 31 widget templates từ DB |
| `GET` | `/forms/admin/widget-catalog?category=healthcare` | Lọc theo category |
| `GET` | `/forms/admin/screens/{moduleCode}` | Danh sách screens (admin, all status) |
| `POST` | `/forms/admin/screens` | Tạo screen (body gồm moduleCode) |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}` | Cập nhật screen |
| `DELETE` | `/forms/admin/screens/{moduleCode}/{screenCode}` | Xóa screen |
| `POST` | `/forms/admin/screens/{moduleCode}/{screenCode}/publish` | Publish screen |
| `POST` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs` | Thêm tab |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | Cập nhật tab |
| `DELETE` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | Xóa tab |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}/widgets` | **Lưu canvas** (full replacement) |

### Admin — Pages (URL "pages" — nhất quán với public `/forms/{moduleCode}/pages`)

> Cùng data với `admin/screens` nhưng `moduleCode` nằm trong URL; dành cho frontend khi đã biết module.

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/forms/admin/{moduleCode}/pages` | Danh sách pages của module (all status) |
| `POST` | `/forms/admin/{moduleCode}/pages` | **Tạo page** (body: code, title, description, sortOrder) |
| `PUT` | `/forms/admin/{moduleCode}/pages/{pageCode}` | Cập nhật page |
| `DELETE` | `/forms/admin/{moduleCode}/pages/{pageCode}` | Xóa page |
| `POST` | `/forms/admin/{moduleCode}/pages/{pageCode}/publish` | Publish page |
| `POST` | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs` | Thêm tab |
| `PUT` | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs/{tabId}` | Cập nhật tab |
| `DELETE` | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs/{tabId}` | Xóa tab |
| `PUT` | `/forms/admin/{moduleCode}/pages/{pageCode}/tabs/{tabId}/widgets` | Lưu canvas (full replacement) |

---

## Response mẫu

### GET /forms/modules

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "code": "tiep-nhan",
      "name": "Tiếp nhận bệnh nhân",
      "description": null,
      "status": "Active",
      "formCount": 3,
      "screenCount": 2,
      "pages": [
        {
          "id": "uuid",
          "moduleCode": "tiep-nhan",
          "code": "dashboard",
          "title": "Dashboard tiếp nhận",
          "description": null,
          "status": "Published",
          "sortOrder": 0,
          "tabCount": 0,
          "createdAtUtc": "2026-06-03T..."
        }
      ],
      "createdAtUtc": "2026-06-01T..."
    }
  ]
}
```

### GET /forms/{moduleCode}/pages

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "moduleCode": "tiep-nhan",
      "code": "dashboard",
      "title": "Dashboard tiếp nhận",
      "description": null,
      "status": "Published",
      "sortOrder": 0,
      "tabCount": 0,
      "createdAtUtc": "2026-06-03T..."
    }
  ]
}
```

### GET /forms/{moduleCode}/{formKey}/schema (BDUI)

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "moduleCode": "tiep-nhan",
    "formKey": "phieu-tiep-nhan",
    "name": "Phiếu tiếp nhận bệnh nhân",
    "version": 2,
    "fields": [
      {
        "key": "ho_ten",
        "label": "Họ và tên",
        "type": "text",
        "order": 0,
        "required": true,
        "width": "full",
        "validationRules": [
          { "type": "minLength", "value": "2", "errorMessage": "Tên phải có ít nhất 2 ký tự" }
        ]
      }
    ],
    "settings": {
      "submitButtonLabel": "Tiếp nhận",
      "successMessage": "Đã tiếp nhận thành công",
      "allowMultipleSubmissions": false
    }
  }
}
```

### PUT /forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}/widgets

```json
[
  {
    "widgetKey": "form-tiep-nhan",
    "widgetType": "FormSection",
    "gridX": 0, "gridY": 0, "gridW": 8, "gridH": 10,
    "configJson": "{}",
    "referenceId": "uuid-of-form-template"
  },
  {
    "widgetKey": "welcome-text",
    "widgetType": "TextBlock",
    "gridX": 8, "gridY": 0, "gridW": 4, "gridH": 3,
    "configJson": "{\"content\": \"Chào mừng!\", \"align\": \"center\"}"
  }
]
```

### GET /forms/screens/{moduleCode}/{screenCode}/layout (SDUI)

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "moduleCode": "tiep-nhan",
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
            "widgetKey": "form-tiep-nhan",
            "widgetType": "FormSection",
            "gridX": 0, "gridY": 0, "gridW": 8, "gridH": 10,
            "config": null,
            "referenceId": "uuid",
            "formSchema": { "formKey": "...", "fields": [...] }
          }
        ]
      }
    ],
    "generatedAt": "2026-06-03T..."
  }
}
```

---

## Screen Designer — Widget Types

| widgetType | Mô tả | Default W×H |
|---|---|---|
| `FormSection` | Nhúng FormTemplate vào vị trí | 6×8 |
| `TextBlock` | Tiêu đề / markdown | 6×2 |
| `Divider` | Đường ngang phân cách | 12×1 |
| `ImageBlock` | Hình ảnh tĩnh từ URL | 4×4 |
| `ConditionalSection` | Container ẩn/hiện theo giá trị field | 6×6 |

Grid dùng **react-grid-layout** (12 cột).

---

## Widget Catalog

31 widget templates chia theo category, seed sẵn trong DB (xem `docs/34-widget-catalog.md`):

| Category | Số widget | Ví dụ |
|----------|-----------|-------|
| `visualization` | 13 | `bar_chart`, `line_chart`, `kpi`, `heatmap` |
| `healthcare` | 11 | `bed_grid`, `alert_list`, `patient_flow_stages`, `news2_bars` |
| `filter` | 4 | `filter_dropdown`, `filter_date_range` |
| `layout` | 2 | `text_widget`, `tab_container` |
| `ai` | 1 | `chat_panel` |

API: `GET /forms/admin/widget-catalog?category={category}`

---

## Database

| Table | Mô tả | JSONB columns |
|-------|-------|--------------|
| `FormModules` | Danh sách module | — |
| `FormTemplates` | Định nghĩa form | `SettingsJson` |
| `FormFields` | Fields của form | `OptionsJson`, `ValidationRulesJson`, `ConditionalLogicJson` |
| `FormSubmissions` | Câu trả lời | `AnswersJson` |
| `FormScreens` | Màn hình SDUI | — |
| `FormScreenTabs` | Tab trong màn hình | — |
| `FormScreenWidgets` | Widget trên canvas | `ConfigJson` |
| `WidgetCatalogs` | Danh mục 31 widget templates | `RequiredColumnsJson`, `OptionalColumnsJson`, `CompatibleWithJson` |

---

## Integration Events

```csharp
// Sau khi form được submit
FormSubmittedIntegrationEvent(
    Guid   SubmissionId,
    Guid   FormTemplateId,
    string ModuleCode,
    string FormKey,
    Guid?  SubmittedBy)

// Sau khi screen được publish
FormScreenPublishedDomainEvent(Guid ScreenId, string ModuleCode, string ScreenCode)
```

Consumers tiềm năng:
- **NotificationService** → gửi thông báo cho bác sĩ phụ trách
- **M01Service** → tạo hồ sơ BenhNhan từ form tiếp nhận

---

## Vị trí code

| Layer | File |
|-------|------|
| Domain Entities | `Domain/Entities/FormModule.cs`, `FormTemplate.cs`, `FormField.cs`, `FormSubmission.cs` |
| Domain Entities | `Domain/Entities/FormScreen.cs`, `FormScreenTab.cs`, `FormScreenWidget.cs`, `WidgetCatalog.cs` |
| Domain Repos | `Domain/Repositories/IFormModuleRepository.cs`, `IFormTemplateRepository.cs` |
| Domain Repos | `Domain/Repositories/IFormScreenRepository.cs`, `IWidgetCatalogRepository.cs` |
| BDUI Schema | `Application/Features/Forms/GetSchema/GetFormSchemaQuery.cs` |
| Module list | `Application/Features/Modules/GetModules/GetModulesQuery.cs` |
| Pages list | `Application/Features/Screens/GetPublishedScreensByModule/GetPublishedScreensByModuleQuery.cs` |
| Drag-drop save | `Application/Features/Tabs/SaveTabWidgets/SaveTabWidgetsCommand.cs` |
| Widget Catalog | `Application/Features/WidgetCatalog/GetWidgetCatalog/GetWidgetCatalogQuery.cs` |
| SDUI Layout | `Application/Features/Screens/GetScreenLayout/GetScreenLayoutQuery.cs` |
| Migrations | `Infrastructure/Persistence/Migrations/20260601090313_InitialCreate.cs` |
| Migrations | `Infrastructure/Persistence/Migrations/20260603021333_AddScreenDesigner.cs` |
| Migrations | `Infrastructure/Persistence/Migrations/20260603130000_AddWidgetCatalog.cs` |
| Public API | `API/Controllers/FormsController.cs`, `ScreensController.cs` |
| Admin API | `API/Controllers/AdminFormsController.cs`, `AdminScreensController.cs` |

---

## Luồng hoạt động

### Frontend render form (BDUI)

```
GET /forms/modules
  → chọn module → GET /forms/{code}           (danh sách forms)
  → chọn form   → GET /forms/{code}/{key}/schema  (schema)
  → render form theo fields
  → POST /forms/{code}/{key}/submit
```

### Frontend render màn hình (SDUI)

```
GET /forms/modules
  → chọn module → GET /forms/{code}/pages         (danh sách screens)
  → chọn screen → GET /forms/screens/{code}/{screenCode}/layout
  → render tabs + widgets theo widgetType
```

### Admin thiết kế screen

```
GET /forms/admin/widget-catalog         → load widget palette
POST /forms/admin/screens               → tạo screen
POST /forms/admin/screens/.../tabs      → thêm tab
[kéo thả widget vào canvas]
PUT  /forms/admin/screens/.../tabs/{id}/widgets  → lưu canvas
POST /forms/admin/screens/.../publish   → publish
```

---

## Chạy local

```bash
docker compose up -d postgres-df dynamicformservice
```

Swagger: `http://localhost:5000/forms/swagger`

Migration (nếu cần chạy tay):
```bash
dotnet ef database update \
  --project src/Services/DynamicFormService/DynamicFormService.Infrastructure \
  --startup-project src/Services/DynamicFormService/DynamicFormService.API
```
