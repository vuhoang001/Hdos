using System.Text.Json;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.GetScreenLayout;

public sealed record GetScreenLayoutQuery(string ModuleCode, string ScreenCode) : IRequest<Result<ScreenLayoutDto>>;

public sealed class GetScreenLayoutQueryHandler(
    IFormScreenRepository   screens,
    IFormTemplateRepository templates,
    IProviderRepository     providers,
    IOperationRepository    operations)
    : IRequestHandler<GetScreenLayoutQuery, Result<ScreenLayoutDto>>
{
    public async Task<Result<ScreenLayoutDto>> Handle(GetScreenLayoutQuery request, CancellationToken ct)
    {
        var screen = await screens.GetWithTabsAndWidgetsAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure<ScreenLayoutDto>(
                Error.NotFound($"Screen '{request.ScreenCode}' không tồn tại hoặc chưa publish."));

        var tabs = new List<ScreenLayoutTabDto>();

        foreach (var tab in screen.Tabs.OrderBy(t => t.SortOrder))
        {
            var widgets = new List<ScreenLayoutWidgetDto>();

            foreach (var w in tab.Widgets)
            {
                FormSchemaDto? formSchema = null;

                if (w.WidgetType.Equals("FormSection", StringComparison.OrdinalIgnoreCase) && w.ReferenceId.HasValue)
                {
                    var template = await templates.GetByIdAsync(w.ReferenceId.Value, includeFields: true, ct);
                    if (template is not null)
                        formSchema = HydrateFormSchema(template);
                }

                object? config = null;
                if (!string.IsNullOrWhiteSpace(w.ConfigJson) && w.ConfigJson != "{}")
                {
                    try
                    {
                        config = JsonSerializer.Deserialize<object>(w.ConfigJson);
                    }
                    catch
                    {
                        config = null;
                    }
                }

                widgets.Add(new ScreenLayoutWidgetDto(
                                w.WidgetKey, w.WidgetType,
                                w.GridX, w.GridY, w.GridW, w.GridH,
                                config, w.ReferenceId, formSchema));
            }

            tabs.Add(new ScreenLayoutTabDto(tab.Id, tab.Label, tab.Slug, tab.SortOrder, tab.IsDefault, widgets));
        }

        var rawSources = DeserializeOrNull<List<Domain.ValueObjects.DataSource>>(screen.DataSourcesJson) ?? new();
        var dataSources = new List<DataSourceDto>(rawSources.Count);
        foreach (var d in rawSources)
        {
            dataSources.Add(await ResolveDataSourceAsync(d, ct));
        }

        return Result.Success(new ScreenLayoutDto(
                                  screen.Id, screen.ModuleCode, screen.Code, screen.Title, screen.Description,
                                  dataSources, tabs, DateTime.UtcNow));
    }

    // Resolve DataSource VO → DataSourceDto. Có 2 nhánh:
    // - MANAGED (OperationId): lookup catalog, gộp Provider.BaseUrl + Operation.Pattern/SchemaPath.
    //   Operation/Provider Inactive → vẫn trả về để FE thấy nhưng có thể skip.
    // - LEGACY: copy field thẳng.
    private async Task<DataSourceDto> ResolveDataSourceAsync(
        Domain.ValueObjects.DataSource d,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.OperationId))
        {
            // LEGACY mode
            return new DataSourceDto(
                d.Namespace, d.ServiceId, d.ResourcePath, d.RequiredParams,
                d.SchemaPath, BaseUrl: null, Kind: null, OperationId: null);
        }

        var parts = d.OperationId.Split("::", 2);
        if (parts.Length != 2)
        {
            return new DataSourceDto(d.Namespace, d.ServiceId, d.ResourcePath, d.RequiredParams,
                d.SchemaPath, BaseUrl: null, Kind: null, OperationId: d.OperationId);
        }

        var providerCode = parts[0];
        var operationKey = parts[1];

        var provider  = await providers.GetByCodeAsync(providerCode, ct);
        var operation = await operations.GetByKeyAsync(providerCode, operationKey, ct);

        // Catalog bị xóa giữa chừng — fallback về thông tin còn lại trong DataSource (có thể null hết).
        if (provider is null || operation is null)
        {
            return new DataSourceDto(
                d.Namespace, d.ServiceId, d.ResourcePath, d.RequiredParams,
                d.SchemaPath, BaseUrl: null, Kind: null, OperationId: d.OperationId);
        }

        // Required params: ưu tiên giá trị admin set khi tạo DataSource (cho phép override),
        // fallback về danh sách trong Operation.
        var requiredParams = d.RequiredParams is { Count: > 0 }
            ? d.RequiredParams
            : operation.GetRequiredParams();

        return new DataSourceDto(
            Namespace:      d.Namespace,
            ServiceId:      provider.Code,
            ResourcePath:   operation.Pattern,
            RequiredParams: requiredParams,
            SchemaPath:     operation.SchemaPath,
            BaseUrl:        provider.BaseUrl,
            Kind:           operation.Kind.ToString(),
            OperationId:    operation.GetCombinedRef());
    }

    private static FormSchemaDto HydrateFormSchema(Domain.Entities.FormTemplate t)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<Domain.ValueObjects.FormSettings>(t.SettingsJson)
            ?? new Domain.ValueObjects.FormSettings("Gửi", "Đã gửi thành công", true);

        var fields = t.Fields.OrderBy(f => f.Order).Select(f => new FormFieldDto(
                                                               f.Id, f.Key, f.Label, f.FieldType.ToString(), f.Order,
                                                               f.Required,
                                                               f.Width.ToString(), f.Placeholder, f.HelpText,
                                                               DeserializeOrNull<List<Domain.ValueObjects.FieldOption>>(
                                                                       f.OptionsJson)
                                                                   ?.Select(o => new FieldOptionDto(o.Label, o.Value))
                                                                   .ToList(),
                                                               DeserializeOrNull<
                                                                       List<Domain.ValueObjects.ValidationRule>>(
                                                                       f.ValidationRulesJson)
                                                                   ?.Select(r => new ValidationRuleDto(
                                                                                r.Type, r.Value, r.ErrorMessage))
                                                                   .ToList(),
                                                               DeserializeOrNull<Domain.ValueObjects.ConditionalLogic>(
                                                                   f.ConditionalLogicJson) is { } cl
                                                                   ? new ConditionalLogicDto(
                                                                       cl.SourceFieldKey, cl.Operator.ToString(),
                                                                       cl.Value, cl.Action.ToString())
                                                                   : null,
                                                               DeserializeOrNull<DataBindingDto>(f.DataBindingJson),
                                                               f.IsReadOnly)).ToList();

        return new FormSchemaDto(t.Id, t.ModuleCode, t.Key, t.Name, t.Description, t.Version, fields,
                                 new FormSettingsDto(settings.SubmitButtonLabel, settings.SuccessMessage,
                                                     settings.AllowMultipleSubmissions));
    }

    private static T? DeserializeOrNull<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return null;
        }
    }
}