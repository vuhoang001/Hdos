# 34 — Widget Catalog (DynamicFormService)

## Mục đích

Widget Catalog là danh mục các loại widget có thể kéo thả vào Screen Designer. Dữ liệu được seed sẵn trong migration và phục vụ qua API:

```
GET /forms/admin/widget-catalog?category={category}
```

## Cấu trúc dữ liệu

| Field | Kiểu | Mô tả |
|-------|------|--------|
| `chartType` | string | Unique key của widget (VD: `bar_chart`, `kpi_card`) |
| `category` | string | Nhóm: `visualization`, `filter`, `layout`, `healthcare`, `ai` |
| `label` | string | Tên hiển thị |
| `description` | string | Mô tả ngắn |
| `icon` | string | Icon name (Ant Design Icons) |
| `requiredColumns` | string[] | Các cột dữ liệu bắt buộc |
| `optionalColumns` | string[] | Các cột dữ liệu tùy chọn |
| `compatibleWith` | string[] | Các nguồn dữ liệu tương thích |
| `sortOrder` | int | Thứ tự hiển thị |

## Danh mục widget (31 widget)

### Visualization (13)
`bar_chart`, `line_chart`, `pie_chart`, `donut_chart`, `area_chart`, `scatter_plot`, `radar_chart`, `heatmap`, `treemap`, `funnel_chart`, `kpi_card`, `data_table`, `metric_card`

### Filter (4)
`date_range_filter`, `dropdown_filter`, `search_filter`, `multi_select_filter`

### Layout (2)
`divider`, `text_block`

### Healthcare (11)
`patient_flow`, `bed_occupancy`, `vital_signs_chart`, `medication_tracker`, `appointment_calendar`, `lab_results_table`, `diagnosis_chart`, `staff_schedule`, `icu_monitor`, `er_queue`, `ward_map`

### AI (1)
`ai_insight`

## Files

| File | Layer | Vai trò |
|------|-------|---------|
| `DynamicFormService.Domain/Entities/WidgetCatalog.cs` | Domain | Entity + JSON deserialization helpers |
| `DynamicFormService.Domain/Repositories/IWidgetCatalogRepository.cs` | Domain | Repository interface |
| `DynamicFormService.Infrastructure/Persistence/Configurations/WidgetCatalogConfiguration.cs` | Infrastructure | EF Core mapping, JSONB columns |
| `DynamicFormService.Infrastructure/Persistence/WidgetCatalogRepository.cs` | Infrastructure | Repository implementation |
| `DynamicFormService.Infrastructure/Persistence/Migrations/20260603130000_AddWidgetCatalog.cs` | Infrastructure | Tạo bảng + seed 31 widget |
| `DynamicFormService.Application/Features/WidgetCatalog/GetWidgetCatalog/GetWidgetCatalogQuery.cs` | Application | CQRS Query handler |

## Database

- Table: `WidgetCatalogs` (PostgreSQL — `postgres-df`)
- Array fields (`RequiredColumnsJson`, `OptionalColumnsJson`, `CompatibleWithJson`) dùng kiểu `jsonb`
- Index unique trên `ChartType`, index thường trên `Category`

## API

```
GET /forms/admin/widget-catalog
GET /forms/admin/widget-catalog?category=visualization
```

Response:
```json
{
  "success": true,
  "data": [
    {
      "chartType": "bar_chart",
      "category": "visualization",
      "label": "Bar Chart",
      "description": "Biểu đồ cột so sánh dữ liệu theo danh mục",
      "icon": "BarChartOutlined",
      "requiredColumns": ["category", "value"],
      "optionalColumns": ["series", "color"],
      "compatibleWith": ["sql", "api", "csv"],
      "sortOrder": 1
    }
  ]
}
```
