# 34 — Widget Catalog (DataMatchingService)

## Mục đích

Widget Catalog là danh mục các loại widget có thể dùng trong dashboard builder. Được seed sẵn 31 widget types khi chạy migration — không cần admin tạo thủ công.

---

## Entity

```
DataMatchingService.Domain/Entities/WidgetCatalog.cs
```

| Field | Type | Mô tả |
|-------|------|-------|
| `Id` | Guid | Primary key (deterministic, fixed) |
| `ChartType` | string (100) | Định danh duy nhất, VD: `line_chart` |
| `Category` | string (50) | Nhóm: `visualization`, `filter`, `layout`, `healthcare`, `ai` |
| `Label` | string (200) | Tên hiển thị tiếng Việt |
| `Description` | string (1000) | Mô tả ngắn |
| `Icon` | string (100) | Tên icon (lucide-react) |
| `RowSchema` | jsonb | Schema mặc định `{}` |
| `RequiredColumnsJson` | jsonb | Các column bắt buộc |
| `OptionalColumnsJson` | jsonb | Các column tùy chọn |
| `CompatibleWithJson` | jsonb | Các chartType có thể chuyển đổi qua lại |
| `SortOrder` | int | Thứ tự hiển thị |

---

## Widget Types (31 total)

### visualization (13)
| chartType | Label |
|-----------|-------|
| `line_chart` | Biểu đồ đường |
| `bar_chart` | Biểu đồ cột |
| `area_chart` | Biểu đồ vùng |
| `pie_chart` | Biểu đồ tròn |
| `donut_chart` | Biểu đồ vòng |
| `kpi` | KPI đơn |
| `gauge` | Đồng hồ đo |
| `heatmap` | Bản đồ nhiệt |
| `scatter` | Biểu đồ phân tán |
| `advanced_table` | Bảng nâng cao |
| `simple_table` | Bảng đơn giản |
| `pivot_table` | Bảng pivot |
| `funnel` | Biểu đồ phễu |

### filter (4)
| chartType | Label |
|-----------|-------|
| `filter_dropdown` | Bộ lọc danh sách |
| `filter_date_range` | Bộ lọc ngày |
| `filter_slider` | Bộ lọc số |
| `filter_search` | Ô tìm kiếm |

### layout (2)
| chartType | Label |
|-----------|-------|
| `text_widget` | Văn bản / Markdown |
| `tab_container` | Tab container |

### healthcare (11)
| chartType | Label |
|-----------|-------|
| `kpi_grid` | Lưới KPI |
| `progress_rows` | Thanh tiến trình |
| `flow_steps` | Luồng bước |
| `timeline_vertical` | Timeline dọc |
| `alert_list` | Danh sách cảnh báo |
| `bed_grid` | Lưới giường bệnh |
| `room_status_grid` | Trạng thái phòng |
| `map_pins` | Bản đồ ghim vị trí |
| `patient_flow_stages` | Luồng bệnh nhân |
| `risk_tiers` | Phân tầng nguy cơ |
| `news2_bars` | NEWS2 Score |

### ai (1)
| chartType | Label |
|-----------|-------|
| `chat_panel` | AI Chatbot |

---

## API

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/dm/widget-catalog` | Toàn bộ catalog, sắp xếp theo sortOrder |
| `GET` | `/dm/widget-catalog?category=healthcare` | Lọc theo category |

### Response mẫu

```json
{
  "success": true,
  "data": [
    {
      "chartType": "line_chart",
      "category": "visualization",
      "label": "Biểu đồ đường",
      "description": "Xu hướng theo thời gian hoặc danh mục. Hỗ trợ nhiều series.",
      "icon": "TrendingUp",
      "requiredColumns": ["x", "y"],
      "optionalColumns": ["series", "color", "annotations"],
      "compatibleWith": ["bar_chart", "area_chart"],
      "sortOrder": 10
    }
  ]
}
```

---

## Migration

File: `20260603120000_AddWidgetCatalog.cs`

- Tạo bảng `WidgetCatalogs`
- Insert 31 widget rows với GUIDs cố định (`00000000-0000-0000-0000-000000000001` → `...0031`)
- Index unique trên `ChartType`, index thường trên `Category`

```bash
dotnet ef database update \
  --project DataMatchingService.Infrastructure \
  --startup-project DataMatchingService.API
```

---

## Vị trí code

| Layer | File |
|-------|------|
| Domain | `DataMatchingService.Domain/Entities/WidgetCatalog.cs` |
| Domain | `DataMatchingService.Domain/Repositories/IWidgetCatalogRepository.cs` |
| Application | `DataMatchingService.Application/Features/WidgetCatalog/GetWidgetCatalogQuery.cs` |
| Infrastructure | `DataMatchingService.Infrastructure/Persistence/WidgetCatalogRepository.cs` |
| Infrastructure | `DataMatchingService.Infrastructure/Persistence/Configurations/WidgetCatalogConfiguration.cs` |
| Infrastructure | `DataMatchingService.Infrastructure/Persistence/Migrations/20260603120000_AddWidgetCatalog.cs` |
| API | `DataMatchingService.API/Controllers/WidgetCatalogController.cs` |
