using System.Text.Json.Serialization;
using Hdos.DataMatchingService.Application.Sdui;

namespace Hdos.DataMatchingService.Application.Sdui;

public sealed record SduiAction(
    [property: JsonPropertyName("label")]   string Label,
    [property: JsonPropertyName("variant")] string Variant,   // "primary" | "default" | "danger"
    [property: JsonPropertyName("color")]   string? Color);

public sealed record SduiRow(
    [property: JsonPropertyName("components")] List<SduiComponent> Components);

public sealed record SduiPage(
    [property: JsonPropertyName("code")]        string Code,
    [property: JsonPropertyName("title")]       string Title,
    [property: JsonPropertyName("badge")]       string? Badge,
    [property: JsonPropertyName("live")]        bool Live,
    [property: JsonPropertyName("subtitle")]    string? Subtitle,
    [property: JsonPropertyName("actions")]     List<SduiAction> Actions,
    [property: JsonPropertyName("rows")]        List<SduiRow> Rows,
    [property: JsonPropertyName("generatedAt")] DateTime GeneratedAt);
