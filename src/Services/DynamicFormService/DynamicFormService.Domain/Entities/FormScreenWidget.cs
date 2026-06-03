using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormScreenWidget : BaseEntity<Guid>
{
    public Guid       TabId      { get; private set; }
    public string     WidgetKey  { get; private set; } = null!;
    public string     WidgetType { get; private set; } = null!;

    // Grid position (react-grid-layout)
    public int GridX { get; private set; }
    public int GridY { get; private set; }
    public int GridW { get; private set; }
    public int GridH { get; private set; }

    // Cấu hình hiển thị: màu, icon, content tùy widgetType
    public string ConfigJson { get; private set; } = null!;

    // Nếu WidgetType = FormSection, trỏ tới FormTemplate.Id
    public Guid? ReferenceId { get; private set; }

    private FormScreenWidget() { }

    public static FormScreenWidget Create(
        Guid    tabId,
        string  widgetKey,
        string  widgetType,
        int     gridX,
        int     gridY,
        int     gridW,
        int     gridH,
        string  configJson,
        Guid?   referenceId = null)
    {
        if (string.IsNullOrWhiteSpace(widgetKey))
            throw new ArgumentException("Widget key is required.", nameof(widgetKey));

        return new FormScreenWidget
        {
            Id          = Guid.NewGuid(),
            TabId       = tabId,
            WidgetKey   = widgetKey.Trim(),
            WidgetType  = widgetType,
            GridX       = gridX,
            GridY       = gridY,
            GridW       = gridW < 1 ? 6  : gridW,
            GridH       = gridH < 1 ? 4  : gridH,
            ConfigJson  = string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson,
            ReferenceId = referenceId
        };
    }
}
