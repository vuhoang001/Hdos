using System.Text.Json.Serialization;

namespace Hdos.DataMatchingService.Application.Dashboard;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KpiGridSection),  "kpi-grid")]
[JsonDerivedType(typeof(PieChartSection), "pie-chart")]
[JsonDerivedType(typeof(BarChartSection), "bar-chart")]
[JsonDerivedType(typeof(TableSection),    "table")]
public abstract record DashboardSection(string Id, string Title);

// --- KPI Grid ---

public sealed record KpiItem(
    string Label,
    double Value,
    string? Unit,
    string Format);   // "number" | "percent" | "currency" | "days"

public sealed record KpiGridSection(
    string Id,
    string Title,
    List<KpiItem> Items)
    : DashboardSection(Id, Title);

// --- Pie Chart ---

public sealed record PieSlice(string Label, int SoLuong, double PhanTram);

public sealed record PieChartSection(
    string Id,
    string Title,
    List<PieSlice> Data)
    : DashboardSection(Id, Title);

// --- Bar Chart ---

public sealed record BarItem(string Label, int SoLuong);

public sealed record BarChartSection(
    string Id,
    string Title,
    List<BarItem> Data)
    : DashboardSection(Id, Title);

// --- Table ---

public sealed record TableColumn(string Key, string Label, string Type);
// Type: "string" | "number" | "currency" | "date" | "badge"

public sealed record TableSection(
    string Id,
    string Title,
    List<TableColumn> Columns,
    List<Dictionary<string, object?>> Rows)
    : DashboardSection(Id, Title);
