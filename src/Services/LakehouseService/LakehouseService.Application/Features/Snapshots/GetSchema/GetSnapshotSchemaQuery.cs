using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Domain.Errors;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.Snapshots.GetSchema;

public sealed record GetSnapshotSchemaQuery(string Namespace) : IRequest<Result<DataSourceSchemaDto>>;

public sealed class GetSnapshotSchemaValidator : AbstractValidator<GetSnapshotSchemaQuery>
{
    public GetSnapshotSchemaValidator()
    {
        RuleFor(x => x.Namespace).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetSnapshotSchemaHandler(ILakehouseSnapshotRepository repository)
    : IRequestHandler<GetSnapshotSchemaQuery, Result<DataSourceSchemaDto>>
{
    public async Task<Result<DataSourceSchemaDto>> Handle(GetSnapshotSchemaQuery request, CancellationToken ct)
    {
        // Lấy snapshot mới nhất trong namespace → introspect top-level keys của Payload.
        // FE dùng cái này để hiển thị dropdown field khi config DataBinding.
        var snapshots = await repository.GetByNamespaceAsync(request.Namespace, 1, ct);
        if (snapshots.Count == 0)
            return Result.Failure<DataSourceSchemaDto>(LakehouseErrors.SnapshotNotFound);

        var latest = snapshots[0];

        var fields = new List<DataSourceFieldDto>();
        try
        {
            using var doc = JsonDocument.Parse(latest.Payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    fields.Add(new DataSourceFieldDto(
                        Key: prop.Name,
                        Type: InferType(prop.Value),
                        Label: null,
                        SourceField: null));
                }
            }
        }
        catch (JsonException)
        {
            // Payload không phải JSON object — trả về schema rỗng.
        }

        fields = fields.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();

        return new DataSourceSchemaDto(
            Namespace: latest.Namespace,
            BusinessKeyField: "businessKey",
            Fields: fields);
    }

    private static string InferType(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Number                      => "number",
        JsonValueKind.String                      => LooksLikeDate(el.GetString()) ? "date" : "string",
        _                                         => "string"
    };

    private static readonly Regex IsoDatePrefix = new(@"^\d{4}-\d{2}-\d{2}", RegexOptions.Compiled);

    private static bool LooksLikeDate(string? s) =>
        !string.IsNullOrEmpty(s) && IsoDatePrefix.IsMatch(s);
}
