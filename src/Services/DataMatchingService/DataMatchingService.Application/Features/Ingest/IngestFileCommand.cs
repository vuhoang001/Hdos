using System.Text.Json;
using FluentValidation;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Ingest;

public sealed record IngestFileCommand(
    string SourceSystem,
    Stream FileStream,
    string FileName,
    string? BusinessKeyOverride) : IRequest<Result<IngestBatchResultDto>>;

public sealed class IngestFileValidator : AbstractValidator<IngestFileCommand>
{
    public IngestFileValidator()
    {
        RuleFor(x => x.SourceSystem).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.FileStream).NotNull();
        RuleFor(x => x.FileName)
            .Must(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .WithMessage("FileName must end with .json or .csv.");
    }
}

public sealed class IngestFileHandler(
    ISourceProfileRepository profiles,
    IStagingRecordRepository records,
    IDataMatchingUnitOfWork uow)
    : IRequestHandler<IngestFileCommand, Result<IngestBatchResultDto>>
{
    public async Task<Result<IngestBatchResultDto>> Handle(IngestFileCommand request, CancellationToken ct)
    {
        var profile = await profiles.GetBySystemAsync(request.SourceSystem, ct);
        if (profile is null)
            return Result.Failure<IngestBatchResultDto>(
                Error.NotFound($"SourceProfile for '{request.SourceSystem}'"));

        var mappings = profile.GetMappings();
        var rawJsonStrings = ParseFile(request.FileStream, request.FileName);

        var stagingRecords = new List<StagingRecord>();

        foreach (var rawJson in rawJsonStrings)
        {
            var canonicalPayload = IngestJsonHandler.ApplyMappings(rawJson, mappings);
            var businessKey = request.BusinessKeyOverride
                ?? IngestJsonHandler.ExtractBusinessKey(canonicalPayload, profile.BusinessKeyField);
            var payloadHash = IngestJsonHandler.ComputeHash(rawJson);

            var isDuplicate = await records.ExistsHashAsync(payloadHash, ct);
            if (isDuplicate) continue;

            stagingRecords.Add(StagingRecord.Receive(
                request.SourceSystem,
                rawJson,
                canonicalPayload,
                businessKey,
                payloadHash));
        }

        if (stagingRecords.Count > 0)
        {
            await records.AddRangeAsync(stagingRecords, ct);
            await uow.SaveChangesAsync(ct);
        }

        return new IngestBatchResultDto(
            stagingRecords.Count,
            stagingRecords.Select(r => r.Id).ToList());
    }

    private static List<string> ParseFile(Stream stream, string fileName)
    {
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return ParseJson(stream);

        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ParseCsv(stream);

        return [];
    }

    private static List<string> ParseJson(Stream stream)
    {
        var result = new List<string>();

        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in doc.RootElement.EnumerateArray())
                result.Add(element.GetRawText());
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            result.Add(doc.RootElement.GetRawText());
        }

        return result;
    }

    private static List<string> ParseCsv(Stream stream)
    {
        var result = new List<string>();

        using var reader = new StreamReader(stream);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine)) return result;

        var headers = headerLine.Split(',').Select(h => h.Trim()).ToArray();

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = line.Split(',');
            var dict = new Dictionary<string, string>();

            for (var i = 0; i < headers.Length; i++)
            {
                var value = i < values.Length ? values[i].Trim() : string.Empty;
                dict[headers[i]] = value;
            }

            result.Add(JsonSerializer.Serialize(dict));
        }

        return result;
    }
}
