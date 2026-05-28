using System.Text.Json.Serialization;

namespace Hdos.DataMatchingService.Application.Sdui;

// Mỗi component trong rows[].components[] đều kế thừa từ đây.
// "type" được tự động thêm vào JSON bởi [JsonPolymorphic].
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KpiCardComponent),      "KpiCard")]
[JsonDerivedType(typeof(ProgressListComponent), "ProgressList")]
[JsonDerivedType(typeof(AlertListComponent),    "AlertList")]
[JsonDerivedType(typeof(FlowPipelineComponent), "FlowPipeline")]
[JsonDerivedType(typeof(ChartPieComponent),     "ChartPie")]
public abstract record SduiComponent(
    [property: JsonPropertyName("span")] int? Span);

// ─────────────────────────────────────────────
// KpiCard — số liệu dạng card
// ─────────────────────────────────────────────

public sealed record KpiCardProps(
    string Title,
    object? Value,       // int hoặc string ("4.23 tỷ")
    string? Accent,      // màu border/icon
    string? Hint,        // dòng phụ bên dưới
    string? HintColor);  // màu hint

public sealed record KpiCardComponent(int? Span, KpiCardProps Props)
    : SduiComponent(Span);

// ─────────────────────────────────────────────
// ProgressList — danh sách thanh tiến trình (vd: công suất giường theo khoa)
// ─────────────────────────────────────────────

public sealed record ProgressItem(
    string Label,
    double Value,             // giá trị hiện tại (vd: 80 = 80%)
    double? SecondaryValue,   // ngưỡng cảnh báo (vd: 90%)
    string? Color);           // màu bar: #ff4d4f | #faad14 | #52c41a

public sealed record FooterAction(string Label, string Variant);

public sealed record ProgressListProps(
    string Title,
    string? HeaderAction,
    int MaxValue,
    List<ProgressItem> Items,
    List<FooterAction>? FooterActions);

public sealed record ProgressListComponent(int? Span, ProgressListProps Props)
    : SduiComponent(Span);

// ─────────────────────────────────────────────
// AlertList — danh sách cảnh báo lâm sàng
// ─────────────────────────────────────────────

public sealed record AlertItem(
    string Code,      // mã chỉ số (vd: "Troponin I")
    string Text,      // nội dung cảnh báo
    string Patient,   // tên bệnh nhân
    string Dept,      // khoa
    string Time,      // thời gian ("3 phút trước")
    string Severity); // "critical" | "warning" | "info"

public sealed record AlertListProps(
    string Title,
    bool RealtimeBadge,
    int? MaxHeight,
    int TotalCount,
    List<AlertItem> Items);

public sealed record AlertListComponent(int? Span, AlertListProps Props)
    : SduiComponent(Span);

// ─────────────────────────────────────────────
// FlowPipeline — dòng bệnh nhân qua các giai đoạn
// ─────────────────────────────────────────────

public sealed record FlowStage(string Label, int Value, string? Color);

public sealed record FlowPipelineProps(
    string Title,
    string? Footer,
    List<FlowStage> Stages);

public sealed record FlowPipelineComponent(int? Span, FlowPipelineProps Props)
    : SduiComponent(Span);

// ─────────────────────────────────────────────
// ChartPie — biểu đồ tròn / donut
// ─────────────────────────────────────────────

public sealed record ChartPieData(string Label, double Value);

public sealed record ChartPieProps(
    string Title,
    int? Height,
    string? Variant,     // "pie" | "donut"
    bool Legend,
    List<ChartPieData> Data,
    List<string>? Colors);

public sealed record ChartPieComponent(int? Span, ChartPieProps Props)
    : SduiComponent(Span);
