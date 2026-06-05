# 42 — Admin API Refactor + Module CRUD

> Tách 3 admin controllers gánh quá nhiều (Forms / Pages / Screens) thành 5 controllers nhỏ theo SRP, xóa duplicate `Pages` vs `Screens`. Cùng commit thêm Module CRUD (Update + Delete) còn thiếu.

---

## 1. Vấn đề trước refactor

### 1.1. `AdminPagesController` duplicate hoàn toàn `AdminScreensController`

9 endpoint cùng feature, 2 URL — cùng gọi cùng Command/Query:

```
/forms/admin/screens/{moduleCode}                      ≡  /forms/admin/{moduleCode}/pages
/forms/admin/screens                                   ≡  /forms/admin/{moduleCode}/pages           (POST)
/forms/admin/screens/{m}/{s}                           ≡  /forms/admin/{m}/pages/{p}                (PUT, DELETE)
/forms/admin/screens/{m}/{s}/publish                   ≡  /forms/admin/{m}/pages/{p}/publish
/forms/admin/screens/{m}/{s}/tabs                      ≡  /forms/admin/{m}/pages/{p}/tabs           (POST)
/forms/admin/screens/{m}/{s}/tabs/{id}                 ≡  /forms/admin/{m}/pages/{p}/tabs/{id}      (PUT, DELETE)
/forms/admin/screens/{m}/{s}/tabs/{id}/widgets         ≡  /forms/admin/{m}/pages/{p}/tabs/{id}/widgets
```

Aggregate entity tên `FormScreen` chứ không phải `FormPage` → "screens" là canonical. Public endpoint `GET /forms/{moduleCode}/pages` (trong `FormsController`) giữ làm alias backward-compat cho client.

### 1.2. `AdminScreensController` chứa endpoint không thuộc Screens

```csharp
[Route("forms/admin/screens")]
public sealed class AdminScreensController {
    [HttpGet("/forms/admin/widget-catalog")]       // ← absolute path, escape prefix
    [HttpPost("/forms/admin/generate-from-source")] // ← cùng vấn đề
}
```

Khó tìm khi search code. Vi phạm SRP.

### 1.3. `AdminFormsController` gánh 3 aggregate roots

Modules + FormTemplates + Submissions trong 1 controller.

---

## 2. Sau refactor — 5 controllers nhỏ

| Controller | Route prefix | Endpoints |
|------------|-------------|-----------|
| `AdminModulesController` | `/forms/admin/modules` | POST, PUT `{code}`, DELETE `{code}` |
| `AdminFormTemplatesController` | `/forms/admin` | POST `modules/{mc}/forms`, POST `forms/{id}/fields`, POST `forms/{id}/publish`, POST `forms/{id}/archive` |
| `AdminSubmissionsController` | `/forms/admin/forms/{id}/submissions` | GET |
| `AdminWidgetCatalogController` | `/forms/admin/widget-catalog` | GET |
| `AdminGenerateController` | `/forms/admin/generate-from-source` | POST |
| `AdminScreensController` (giữ, gọn lại) | `/forms/admin/screens` | Chỉ giữ Screens + DataSources + Tabs + Widgets |
| `AdminProvidersController` (đã có từ doc 41) | `/forms/admin/providers` | CRUD Provider |
| `AdminOperationsController` (đã có) | `/forms/admin/...` | CRUD Operation + list cross-provider |

**Xóa:**
- `AdminFormsController` (split sang 3 controller trên)
- `AdminPagesController` (duplicate full với Screens)

**URL không đổi** cho mọi endpoint chuyển sang controller mới → no breaking change cho FE.

---

## 3. Module CRUD — bổ sung Update + Delete

Trước đây chỉ có `POST /forms/admin/modules`. Giờ đủ vòng đời:

### 3.1. Update module

```
PUT /forms/admin/modules/{moduleCode}
Body: { name, description? }
→ 200 FormModuleDto. 400 nếu module không tồn tại.
```

Chỉ sửa `name` và `description`. Không đổi `code`, không đổi `status` (dùng activate/deactivate riêng nếu cần).

### 3.2. Delete module

```
DELETE /forms/admin/modules/{moduleCode}
→ 200 nếu xóa thành công.
→ 400 Conflict nếu:
   - Module còn FormTemplate → "còn N form. Hãy xóa form trước."
   - Module còn FormScreen   → "còn N screen. Hãy xóa screen trước."
```

Cascade rule: không cho xóa module nếu còn aggregate con — admin phải dọn FormTemplate + FormScreen trước. Tránh dữ liệu mồ côi (orphan).

Implementation: `DeleteModuleCommandHandler` query `IFormTemplateRepository.GetByModuleCodeAsync` và `IFormScreenRepository.GetByModuleAsync` trước khi `Remove`.

### 3.3. `IFormModuleRepository.Remove` mới

```csharp
public interface IFormModuleRepository
{
    // ... existing methods
    void Remove(FormModule module);
}
```

EF Core impl đơn giản: `db.FormModules.Remove(module)`.

---

## 4. Migration impact

| Loại | Trước | Sau | Breaking? |
|------|-------|-----|-----------|
| URL endpoint | 100% giữ nguyên | Map sang controller mới | ❌ Không |
| `/forms/admin/widget-catalog` | Trong AdminScreensController | Controller riêng | ❌ Không |
| `/forms/admin/{mc}/pages/...` | AdminPagesController | **Xóa** | ⚠️ FE phải đổi sang `/forms/admin/screens/...` |
| `PUT /forms/admin/modules/{code}` | Không tồn tại | Mới | ❌ Không (FE chưa dùng) |
| `DELETE /forms/admin/modules/{code}` | Không tồn tại | Mới | ❌ Không |

---

## 5. Files thay đổi

**Tạo mới (5 controllers):**
- `AdminModulesController.cs`
- `AdminFormTemplatesController.cs`
- `AdminSubmissionsController.cs`
- `AdminWidgetCatalogController.cs`
- `AdminGenerateController.cs`

**Xóa:**
- `AdminFormsController.cs`
- `AdminPagesController.cs`

**Sửa:**
- `AdminScreensController.cs` — bỏ WidgetCatalog + Generate methods
- `IFormModuleRepository.cs` — thêm `Remove`
- `FormModuleRepository.cs` — impl `Remove`

**Thêm Application features:**
- `Features/Modules/UpdateModule/UpdateModuleCommand.cs`
- `Features/Modules/DeleteModule/DeleteModuleCommand.cs`

---

## 6. Tham chiếu

- [29 — DynamicFormService](./29-dynamic-form-service.md) — tổng quan service, endpoint list
- [32 — DynamicFormService Spec](./32-dynamic-form-spec.md) — entity + business rule chi tiết
- [41 — Loose Coupling Architecture](./41-loose-coupling-architecture.md) — `AdminProvidersController` + `AdminOperationsController` đã có
