using System.Text.Json.Serialization;

namespace Hdos.LakehouseService.Application.Charts.Sdui;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KpiCardComponent),      "KpiCard")]
[JsonDerivedType(typeof(ProgressListComponent), "ProgressList")]
[JsonDerivedType(typeof(AlertListComponent),    "AlertList")]
[JsonDerivedType(typeof(FlowPipelineComponent), "FlowPipeline")]
[JsonDerivedType(typeof(ChartPieComponent),     "ChartPie")]
public abstract record SduiComponent(
    [property: JsonPropertyName("span")] int? Span);

// KpiCard
public sealed record KpiCardProps(string Title, object? Value, string? Accent, string? Hint, string? HintColor);
public sealed record KpiCardComponent(int? Span, KpiCardProps Props) : SduiComponent(Span);

// ProgressList
public sealed record ProgressItem(string Label, double Value, double? SecondaryValue, string? Color);
public sealed record FooterAction(string Label, string Variant);
public sealed record ProgressListProps(
    string Title, string? HeaderAction, int MaxValue,
    List<ProgressItem> Items, List<FooterAction>? FooterActions);
public sealed record ProgressListComponent(int? Span, ProgressListProps Props) : SduiComponent(Span);

// AlertList
public sealed record AlertItem(string Code, string Text, string Patient, string Dept, string Time, string Severity);
public sealed record AlertListProps(string Title, bool RealtimeBadge, int? MaxHeight, int TotalCount, List<AlertItem> Items);
public sealed record AlertListComponent(int? Span, AlertListProps Props) : SduiComponent(Span);

// FlowPipeline
public sealed record FlowStage(string Label, int Value, string? Color);
public sealed record FlowPipelineProps(string Title, string? Footer, List<FlowStage> Stages);
public sealed record FlowPipelineComponent(int? Span, FlowPipelineProps Props) : SduiComponent(Span);

// ChartPie
public sealed record ChartPieData(string Label, double Value);
public sealed record ChartPieProps(
    string Title, int? Height, string? Variant, bool Legend,
    List<ChartPieData> Data, List<string>? Colors);
public sealed record ChartPieComponent(int? Span, ChartPieProps Props) : SduiComponent(Span);
