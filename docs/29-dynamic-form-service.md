# 29 — DynamicFormService (Backend-Driven UI Forms)

## Mục đích

DynamicFormService cho phép **admin định nghĩa form y tế tại runtime** mà không cần deploy lại code. Frontend nhận JSON schema từ backend và render form hoàn toàn động. Đây là pattern **BDUI (Backend-Driven UI)** — tương tự SDUI đang có trong DataMatchingService nhưng áp dụng cho form nhập liệu hai chiều.

---

## Tư duy thiết kế

### Vấn đề giải quyết

Bệnh viện cần nhiều loại form khác nhau (tiếp nhận, khám, xuất viện, đánh giá...). Trước đây mỗi form là một màn hình hardcode ở frontend. Với DynamicFormService:

- Admin tạo/sửa form qua API → không cần frontend deploy
- Frontend nhận schema → render tự động với bất kỳ field type nào
- Mỗi module nhóm nhiều form liên quan (ví dụ: module "tiep-nhan" có các form nhập viện, cấp cứu, tự nguyện)

### Module → Form → Field hierarchy

```
FormModule          (nhóm logic: "tiep-nhan", "cap-cuu", "noi-tru")
  └── FormTemplate  (form cụ thể: "phieu-tiep-nhan", "phieu-danh-gia")
        └── FormField (field: "ho_ten", "ngay_sinh", "gioi_tinh")
```

### Lifecycle của một form

```
Draft ──publish()──→ Published ──archive()──→ Archived
  ↑                                                |
  └──────────── tạo phiên bản mới ────────────────┘
```

- **Draft**: chỉ admin thấy, có thể sửa field
- **Published**: public có thể submit, không được sửa field
- **Archived**: không nhận submission mới

---

## Kiến trúc

```
src/Services/DynamicFormService/
├── DynamicFormService.Domain/
│   ├── Entities/
│   │   ├── FormModule.cs          ← AggregateRoot<Guid>
│   │   ├── FormTemplate.cs        ← AggregateRoot<Guid> (chứa Fields)
│   │   ├── FormField.cs           ← BaseEntity<Guid>
│   │   ├── FormSubmission.cs      ← AggregateRoot<Guid>
│   │   ├── FormScreen.cs          ← AggregateRoot<Guid> ⭐ Screen Designer
│   │   ├── FormScreenTab.cs       ← BaseEntity<Guid>    ⭐ Screen Designer
│   │   └── FormScreenWidget.cs    ← BaseEntity<Guid>    ⭐ Screen Designer
│   ├── Enums/
│   │   ├── ModuleStatus.cs        ← Active | Inactive
│   │   ├── FormStatus.cs          ← Draft | Published | Archived (dùng chung cho Form và Screen)
│   │   ├── FieldType.cs           ← Text | Textarea | Number | Date | Select | ...
│   │   ├── FieldWidth.cs          ← Full | Half | Third
│   │   ├── SubmissionStatus.cs    ← Submitted | Reviewed
│   │   └── WidgetType.cs          ← FormSection | TextBlock | Divider | ImageBlock | ConditionalSection ⭐
│   ├── ValueObjects/
│   │   ├── FormSettings.cs        ← SubmitButtonLabel, SuccessMessage, AllowMultiple
│   │   ├── FieldOption.cs         ← Label, Value (cho Select/Radio/Checkbox)
│   │   ├── ValidationRule.cs      ← Type, Value, ErrorMessage
│   │   ├── ConditionalLogic.cs    ← SourceFieldKey, Operator, Value, Action
│   │   └── FieldAnswer.cs         ← FieldKey, Value
│   ├── Events/
│   │   ├── FormModuleCreatedDomainEvent.cs
│   │   ├── FormPublishedDomainEvent.cs
│   │   ├── FormSubmittedDomainEvent.cs
│   │   └── FormScreenPublishedDomainEvent.cs ⭐
│   └── Repositories/
│       ├── IFormModuleRepository.cs
│       ├── IFormTemplateRepository.cs
│       ├── IFormSubmissionRepository.cs
│       ├── IFormScreenRepository.cs          ⭐
│       └── IDynamicFormUnitOfWork.cs
├── DynamicFormService.Application/
│   ├── Features/
│   │   ├── Modules/
│   │   │   ├── CreateModule/CreateModuleCommand.cs
│   │   │   └── GetModules/GetModulesQuery.cs
│   │   ├── Forms/
│   │   │   ├── CreateForm/CreateFormCommand.cs
│   │   │   ├── AddField/AddFieldCommand.cs
│   │   │   ├── PublishForm/PublishFormCommand.cs
│   │   │   ├── ArchiveForm/ArchiveFormCommand.cs
│   │   │   ├── GetSchema/GetFormSchemaQuery.cs  ← ⭐ BDUI form endpoint
│   │   │   └── GetForms/GetFormsByModuleQuery.cs
│   │   ├── Submissions/
│   │   │   ├── SubmitForm/SubmitFormCommand.cs
│   │   │   └── GetSubmissions/GetSubmissionsQuery.cs
│   │   ├── Screens/                             ← ⭐ Screen Designer
│   │   │   ├── CreateScreen/CreateScreenCommand.cs
│   │   │   ├── UpdateScreen/UpdateScreenCommand.cs
│   │   │   ├── DeleteScreen/DeleteScreenCommand.cs
│   │   │   ├── PublishScreen/PublishScreenCommand.cs
│   │   │   ├── GetScreens/GetScreensQuery.cs
│   │   │   └── GetScreenLayout/GetScreenLayoutQuery.cs  ← ⭐ SDUI endpoint chính
│   │   ├── Tabs/                                ← ⭐ Screen Designer
│   │   │   ├── CreateTab/CreateTabCommand.cs
│   │   │   ├── UpdateTab/UpdateTabCommand.cs
│   │   │   ├── DeleteTab/DeleteTabCommand.cs
│   │   │   └── SaveTabWidgets/SaveTabWidgetsCommand.cs  ← ⭐ Drag-and-drop save
│   │   └── WidgetCatalog/
│   │       └── GetWidgetCatalog/GetWidgetCatalogQuery.cs
│   ├── DTOs/DynamicFormDtos.cs
│   └── DependencyInjection.cs
├── DynamicFormService.Infrastructure/
│   ├── Persistence/
│   │   ├── DynamicFormDbContext.cs
│   │   ├── Configurations/           ← EF Core IEntityTypeConfiguration
│   │   ├── FormModuleRepository.cs
│   │   ├── FormTemplateRepository.cs
│   │   ├── FormSubmissionRepository.cs
│   │   ├── FormScreenRepository.cs   ⭐
│   │   └── DynamicFormUnitOfWork.cs
│   └── DependencyInjection.cs
└── DynamicFormService.API/
    ├── Controllers/
    │   ├── FormsController.cs         ← public (form submit/schema)
    │   ├── AdminFormsController.cs    ← admin (form/module CRUD)
    │   ├── AdminScreensController.cs  ← admin (screen designer) ⭐
    │   └── ScreensController.cs       ← public (SDUI layout) ⭐
    ├── Program.cs
    ├── Dockerfile
    └── DynamicFormService.API.csproj
```

---

## Database

**PostgreSQL** (database riêng: `DynamicFormDb`) — dùng JSONB cho dữ liệu schema linh hoạt.

| Table | Mô tả | JSON columns |
|-------|-------|--------------|
| `FormModules` | Danh sách module | — |
| `FormTemplates` | Định nghĩa form | `SettingsJson` (FormSettings) |
| `FormFields` | Fields của form | `OptionsJson`, `ValidationRulesJson`, `ConditionalLogicJson` |
| `FormSubmissions` | Câu trả lời | `AnswersJson` (List\<FieldAnswer\>) |
| `FormScreens` | Màn hình SDUI | — |
| `FormScreenTabs` | Tab trong màn hình | — |
| `FormScreenWidgets` | Widget trên canvas | `ConfigJson` (cấu hình hiển thị) |

**Lý do dùng JSONB cho Options/ValidationRules/ConditionalLogic/Config**: cấu trúc thay đổi theo từng field/widget type, không cần query bên trong. JSONB vẫn indexable khi cần.

### Migration

```bash
cd src/Services/DynamicFormService/DynamicFormService.API
dotnet ef migrations add InitialCreate \
  --project ../DynamicFormService.Infrastructure \
  --startup-project . \
  --output-dir Persistence/Migrations
dotnet ef database update
```

---

## API Endpoints

### Public (anonymous)

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/forms/modules` | Danh sách modules active |
| `GET` | `/forms/{moduleCode}` | Danh sách forms trong module |
| `GET` | `/forms/{moduleCode}/{formKey}/schema` | ⭐ BDUI schema form — frontend dùng để render |
| `POST` | `/forms/{moduleCode}/{formKey}/submit` | Submit form |
| `GET` | `/forms/screens/{moduleCode}` | Danh sách screens đã published ⭐ |
| `GET` | `/forms/screens/{moduleCode}/{screenCode}/layout` | ⭐ SDUI layout — toàn bộ tabs + widgets + form schemas |
| `GET` | `/forms/health` | Health check |

### Admin — Form & Module

| Method | Route | Mô tả |
|--------|-------|-------|
| `POST` | `/forms/admin/modules` | Tạo module mới |
| `POST` | `/forms/admin/modules/{moduleCode}/forms` | Tạo form trong module |
| `POST` | `/forms/admin/forms/{id}/fields` | Thêm field vào form |
| `POST` | `/forms/admin/forms/{id}/publish` | Publish form |
| `POST` | `/forms/admin/forms/{id}/archive` | Archive form |
| `GET` | `/forms/admin/forms/{id}/submissions` | Xem submissions (có phân trang) |

### Admin — Screen Designer ⭐

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/forms/admin/widget-catalog` | Danh sách widget types hỗ trợ |
| `GET` | `/forms/admin/screens/{moduleCode}` | Danh sách screens của module |
| `POST` | `/forms/admin/screens` | Tạo screen mới |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}` | Cập nhật screen |
| `DELETE` | `/forms/admin/screens/{moduleCode}/{screenCode}` | Xóa screen |
| `POST` | `/forms/admin/screens/{moduleCode}/{screenCode}/publish` | Publish screen |
| `POST` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs` | Thêm tab |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | Cập nhật tab |
| `DELETE` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}` | Xóa tab |
| `PUT` | `/forms/admin/screens/{moduleCode}/{screenCode}/tabs/{tabId}/widgets` | ⭐ Lưu canvas (drag-and-drop full replace) |

---

## BDUI Schema Response

`GET /forms/tiep-nhan/phieu-tiep-nhan/schema` trả về:

```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-...",
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
        "placeholder": "Nhập họ và tên đầy đủ",
        "validationRules": [
          { "type": "minLength", "value": "2", "errorMessage": "Tên phải có ít nhất 2 ký tự" }
        ]
      },
      {
        "key": "ngay_sinh",
        "label": "Ngày sinh",
        "type": "date",
        "order": 1,
        "required": true,
        "width": "half"
      },
      {
        "key": "gioi_tinh",
        "label": "Giới tính",
        "type": "radio",
        "order": 2,
        "required": true,
        "width": "half",
        "options": [
          { "label": "Nam", "value": "nam" },
          { "label": "Nữ", "value": "nu" },
          { "label": "Khác", "value": "khac" }
        ]
      },
      {
        "key": "mo_ta_them",
        "label": "Mô tả thêm",
        "type": "textarea",
        "order": 3,
        "required": false,
        "width": "full",
        "conditionalLogic": {
          "sourceFieldKey": "gioi_tinh",
          "operator": "Equals",
          "value": "khac",
          "action": "Show"
        }
      }
    ],
    "settings": {
      "submitButtonLabel": "Tiếp nhận",
      "successMessage": "Đã tiếp nhận bệnh nhân thành công",
      "allowMultipleSubmissions": false
    }
  }
}
```

**Frontend logic**: duyệt `fields` theo `order`, render component theo `type`, áp `width` cho layout grid, evaluate `conditionalLogic` khi giá trị field nguồn thay đổi.

---

## Field Types

| Type | Mô tả | Cần `options` |
|------|-------|:---:|
| `text` | Input một dòng | ✗ |
| `textarea` | Textarea nhiều dòng | ✗ |
| `number` | Số (int/float) | ✗ |
| `date` | Chọn ngày | ✗ |
| `datetime` | Chọn ngày giờ | ✗ |
| `select` | Dropdown chọn 1 | ✓ |
| `multiselect` | Dropdown chọn nhiều | ✓ |
| `radio` | Radio button | ✓ |
| `checkbox` | Checkbox | ✓ |
| `file` | Upload file | ✗ |
| `signature` | Chữ ký tay (canvas) | ✗ |
| `section` | Tiêu đề phân mục | ✗ |

---

## Integration Events

Service publish event sau khi form được submit:

```csharp
// src/BuildingBlocks/Contracts/IntegrationEvents/FormSubmittedIntegrationEvent.cs
FormSubmittedIntegrationEvent(
    Guid   SubmissionId,
    Guid   FormTemplateId,
    string ModuleCode,
    string FormKey,
    Guid?  SubmittedBy)
```

Các service downstream có thể consume:
- **NotificationService** → gửi thông báo cho bác sĩ phụ trách
- **M01Service** → tạo hồ sơ BenhNhan từ form tiếp nhận

---

## Fit vào HDOS

```
Client
  │
  GET /forms/tiep-nhan/phieu-tiep-nhan/schema
  │
nginx (/forms/*) → DynamicFormService:8080
  │
DynamicFormDbContext (PostgreSQL: DynamicFormDb)
  │
  POST /forms/tiep-nhan/phieu-tiep-nhan/submit
  │
DynamicFormService → publishes FormSubmittedIntegrationEvent
  │
  ├── NotificationService (consumes) → gửi thông báo
  └── M01Service (consumes) → tạo BenhNhan record
```

---

## Thêm module/form mới (workflow)

```bash
# 1. Tạo module
POST /forms/admin/modules
{ "code": "noi-tru", "name": "Nội trú" }

# 2. Tạo form trong module
POST /forms/admin/modules/noi-tru/forms
{ "key": "phieu-nhap-vien", "name": "Phiếu nhập viện nội trú" }

# 3. Thêm fields
POST /forms/admin/forms/{formId}/fields
{ "key": "ma_benh_nhan", "label": "Mã bệnh nhân", "type": 0, "order": 0, "required": true, "width": 0 }

# 4. Publish
POST /forms/admin/forms/{formId}/publish

# 5. Frontend gọi schema
GET /forms/noi-tru/phieu-nhap-vien/schema
```

---

## Chạy local

Thêm vào `.env`:
```
POSTGRES_DF_PASSWORD=df_pass
```

```bash
docker-compose up -d postgres-df dynamicformservice
```

Service swagger: `https://localhost:5000/forms/swagger`

---

## Convention notes

- Namespace: `Hdos.DynamicFormService.{Layer}`
- Theo đúng pattern 4-layer (Domain / Application / Infrastructure / API)
- JSONB columns cho dữ liệu schema linh hoạt (Options, ValidationRules, ConditionalLogic, Answers)
- Enum stored as string trong DB (`.HasConversion<string>()`)
- ModuleCode denormalized trong FormTemplate và FormSubmission (tránh JOIN hot path)
- MassTransit EF Outbox (postgres) — đảm bảo exactly-once event publish
