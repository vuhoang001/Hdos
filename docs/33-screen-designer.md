# 33 — Screen Designer

> Xem tổng quan và API đầy đủ tại **[29-dynamic-form-service.md](29-dynamic-form-service.md)**.
> Tài liệu này ghi chi tiết business rules và quy tắc validation riêng của Screen Designer.

---

## Business Rules

### FormScreen
- `code` unique trong cùng `moduleCode`
- Chỉ xóa được screen ở trạng thái `Draft`; đã `Published` phải archive trước
- Publish cascade: screen → status `Published`; không ảnh hưởng tabs/widgets
- Xóa screen cascade xóa toàn bộ tabs và widgets bên trong

### FormScreenTab
- `slug` unique trong cùng screen
- Mỗi screen có đúng **một** tab `isDefault = true`; set tab mới làm default tự động unset tab cũ
- Xóa tab cascade xóa toàn bộ widgets bên trong

### FormScreenWidget — Canvas Save
- Endpoint `PUT .../tabs/{tabId}/widgets` là **full replacement**: xóa toàn bộ widgets cũ, insert danh sách mới
- `widgetKey` unique trong cùng tab
- `widgetType` phải là giá trị hợp lệ của enum `WidgetType`
- `gridW` và `gridH` tối thiểu = 1; nếu truyền < 1 thì default về 6×4
- Nếu `widgetType = FormSection` thì `referenceId` nên trỏ tới `FormTemplate.Id` đang ở trạng thái `Published`

### Widget Types

| widgetType | Default W×H | Ghi chú |
|---|---|---|
| `FormSection` | 6×8 | `referenceId` = FormTemplate.Id |
| `TextBlock` | 6×2 | `configJson.content` là markdown |
| `Divider` | 12×1 | Không cần config |
| `ImageBlock` | 4×4 | `configJson.url` là URL ảnh |
| `ConditionalSection` | 6×6 | `configJson.condition` định nghĩa điều kiện ẩn/hiện |

---

## Validation Rules

```
CreateScreen:  moduleCode NotEmpty, code NotEmpty MaxLength(100), title NotEmpty MaxLength(200)
UpdateScreen:  title NotEmpty MaxLength(200)
CreateTab:     label NotEmpty MaxLength(200), slug NotEmpty MaxLength(100) regex([a-z0-9-]+)
UpdateTab:     label NotEmpty MaxLength(200)
SaveWidgets:   widgets NotNull; mỗi widget: widgetKey NotEmpty, widgetType valid enum, gridW/H > 0; widgetKey unique trong list
```

---

## State Machine

```
FormScreen:
  [any] ──CreateScreen──► Draft
  Draft ──PublishScreen──► Published
  Published ──(manual)──► Archived   (chưa implement endpoint, dùng trực tiếp)
```

---

## Vị trí code

| Layer | File |
|-------|------|
| Domain Entities | `Domain/Entities/FormScreen.cs`, `FormScreenTab.cs`, `FormScreenWidget.cs` |
| Domain Enum | `Domain/Enums/WidgetType.cs` |
| Repo Interface | `Domain/Repositories/IFormScreenRepository.cs` |
| Commands | `Application/Features/Screens/CreateScreen/`, `UpdateScreen/`, `DeleteScreen/`, `PublishScreen/` |
| Commands | `Application/Features/Tabs/CreateTab/`, `UpdateTab/`, `DeleteTab/`, `SaveTabWidgets/` |
| EF Config | `Infrastructure/Persistence/Configurations/FormScreen*.cs` |
| Repo Impl | `Infrastructure/Persistence/FormScreenRepository.cs` |
| Admin API | `API/Controllers/AdminScreensController.cs` |
| Public SDUI | `API/Controllers/ScreensController.cs` |
| Migration | `Infrastructure/Persistence/Migrations/20260603021333_AddScreenDesigner.cs` |
