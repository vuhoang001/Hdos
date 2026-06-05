using FluentValidation;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Sources.GetSchema;

public sealed record GetSourceSchemaQuery(
    string SourceSystem,
    string RecordType) : IRequest<Result<DataSourceSchemaDto>>;

public sealed class GetSourceSchemaValidator : AbstractValidator<GetSourceSchemaQuery>
{
    public GetSourceSchemaValidator()
    {
        RuleFor(x => x.SourceSystem).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RecordType).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetSourceSchemaHandler(ISourceProfileRepository profiles)
    : IRequestHandler<GetSourceSchemaQuery, Result<DataSourceSchemaDto>>
{
    public async Task<Result<DataSourceSchemaDto>> Handle(GetSourceSchemaQuery request, CancellationToken ct)
    {
        var profile = await profiles.GetBySystemAndTypeAsync(request.SourceSystem, request.RecordType, ct);
        if (profile is null)
            return Result.Failure<DataSourceSchemaDto>(
                Error.NotFound($"SourceProfile '{request.SourceSystem}/{request.RecordType}' not found."));

        var mappings = profile.GetMappings();

        // Mỗi entry (sourceField → canonicalKey) => DataSourceFieldDto
        // Type mặc định "string" — FE có thể infer thêm nếu cần.
        var fields = mappings
            .Select(kv => new DataSourceFieldDto(
                Key: kv.Value,
                Type: "string",
                Label: null,
                SourceField: kv.Key))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();

        return new DataSourceSchemaDto(
            Namespace: $"{profile.SourceSystem}/{profile.RecordType}",
            BusinessKeyField: profile.BusinessKeyField,
            Fields: fields);
    }
}
