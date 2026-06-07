using System.Text.Json;
using FluentValidation;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Application.Services;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Ingest;

public sealed record IngestJsonCommand(
    string SourceSystem,
    string RecordType,
    string RawPayload,
    string? BusinessKeyOverride) : IRequest<Result<IngestResultDto>>;

public sealed class IngestJsonValidator : AbstractValidator<IngestJsonCommand>
{
    public IngestJsonValidator()
    {
        RuleFor(x => x.SourceSystem).NotEmpty();
        RuleFor(x => x.RecordType).NotEmpty();
        RuleFor(x => x.RawPayload)
            .NotEmpty()
            .Must(IsValidJson).WithMessage("RawPayload must be valid JSON.");
    }

    private static bool IsValidJson(string payload)
    {
        try { JsonDocument.Parse(payload); return true; }
        catch { return false; }
    }
}

public sealed class IngestJsonHandler(
    IIngestCoreService core,
    IStagingRecordRepository records,
    IDataMatchingUnitOfWork uow)
    : IRequestHandler<IngestJsonCommand, Result<IngestResultDto>>
{
    public async Task<Result<IngestResultDto>> Handle(IngestJsonCommand request, CancellationToken ct)
    {
        var built = await core.TryBuildRecordAsync(
            request.SourceSystem, request.RecordType, request.RawPayload, request.BusinessKeyOverride, ct);

        if (built.IsFailure)
            return Result.Failure<IngestResultDto>(built.Error);

        var record = built.Value;
        if (record is null)
            return Result.Failure<IngestResultDto>(
                Error.Conflict("Duplicate payload: a record with this exact content already exists."));

        await records.AddAsync(record, ct);
        await uow.SaveChangesAsync(ct);

        return new IngestResultDto(record.Id, record.SourceSystem, record.RecordType, record.BusinessKey, record.Status.ToString());
    }
}
