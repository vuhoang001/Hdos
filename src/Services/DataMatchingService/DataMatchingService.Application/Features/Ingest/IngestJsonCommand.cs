using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Ingest;

public sealed record IngestJsonCommand(
    string SourceSystem,
    string RawPayload,
    string? BusinessKeyOverride) : IRequest<Result<IngestResultDto>>;

public sealed class IngestJsonValidator : AbstractValidator<IngestJsonCommand>
{
    public IngestJsonValidator()
    {
        RuleFor(x => x.SourceSystem).NotEmpty();
        RuleFor(x => x.RawPayload)
            .NotEmpty()
            .Must(IsValidJson).WithMessage("RawPayload must be valid JSON.");
    }

    private static bool IsValidJson(string payload)
    {
        try
        {
            JsonDocument.Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class IngestJsonHandler(
    ISourceProfileRepository profiles,
    IStagingRecordRepository records,
    IDataMatchingUnitOfWork uow)
    : IRequestHandler<IngestJsonCommand, Result<IngestResultDto>>
{
    public async Task<Result<IngestResultDto>> Handle(IngestJsonCommand request, CancellationToken ct)
    {
        var profile = await profiles.GetBySystemAsync(request.SourceSystem, ct);
        if (profile is null)
            return Result.Failure<IngestResultDto>(
                Error.NotFound($"SourceProfile for '{request.SourceSystem}'"));

        var mappings = profile.GetMappings();
        var canonicalPayload = ApplyMappings(request.RawPayload, mappings);

        var businessKey = request.BusinessKeyOverride
            ?? ExtractBusinessKey(canonicalPayload, profile.BusinessKeyField);

        var payloadHash = ComputeHash(request.RawPayload);

        var isDuplicate = await records.ExistsHashAsync(payloadHash, ct);
        if (isDuplicate)
            return Result.Failure<IngestResultDto>(
                Error.Conflict("Duplicate payload: a record with this exact content already exists."));

        var record = StagingRecord.Receive(
            request.SourceSystem,
            request.RawPayload,
            canonicalPayload,
            businessKey,
            payloadHash);

        await records.AddAsync(record, ct);
        await uow.SaveChangesAsync(ct);

        return new IngestResultDto(
            record.Id,
            record.SourceSystem,
            record.BusinessKey,
            record.Status.ToString());
    }

    internal static string ApplyMappings(string rawPayload, Dictionary<string, string> mappings)
    {
        using var doc = JsonDocument.Parse(rawPayload);
        var result = new Dictionary<string, JsonElement>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (mappings.TryGetValue(prop.Name, out var canonicalName))
                result[canonicalName] = prop.Value.Clone();
            else
                result[prop.Name] = prop.Value.Clone();
        }

        return JsonSerializer.Serialize(result);
    }

    internal static string? ExtractBusinessKey(string? canonicalPayload, string businessKeyField)
    {
        if (string.IsNullOrEmpty(canonicalPayload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(canonicalPayload);
            if (doc.RootElement.TryGetProperty(businessKeyField, out var val))
                return val.ToString();
        }
        catch
        {
            // ignore parse errors, return null
        }

        return null;
    }

    internal static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
