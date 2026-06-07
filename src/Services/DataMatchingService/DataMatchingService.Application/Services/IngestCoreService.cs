using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;

namespace Hdos.DataMatchingService.Application.Services;

public sealed class IngestCoreService(
    ISourceProfileRepository profiles,
    IStagingRecordRepository records) : IIngestCoreService
{
    public async Task<Result<StagingRecord?>> TryBuildRecordAsync(
        string  sourceSystem,
        string  recordType,
        string  rawPayloadJson,
        string? businessKeyOverride,
        CancellationToken ct)
    {
        var profile = await profiles.GetBySystemAndTypeAsync(sourceSystem, recordType, ct);
        if (profile is null)
            return Result.Failure<StagingRecord?>(
                Error.NotFound($"SourceProfile '{sourceSystem}/{recordType}' not found."));

        var mappings         = profile.GetMappings();
        var canonicalPayload = ApplyMappings(rawPayloadJson, mappings);
        var businessKey      = businessKeyOverride
                               ?? ExtractBusinessKey(canonicalPayload, profile.BusinessKeyField);
        var payloadHash      = ComputeHash(rawPayloadJson);

        if (await records.ExistsHashAsync(payloadHash, ct))
            return Result.Success<StagingRecord?>(null);

        var record = StagingRecord.Receive(
            sourceSystem,
            recordType,
            rawPayloadJson,
            canonicalPayload,
            businessKey,
            payloadHash);

        return Result.Success<StagingRecord?>(record);
    }

    // Đổi tên field của JSON gốc sang tên chuẩn theo bảng mapping.
    // Field không có trong mapping → giữ nguyên, không mất dữ liệu.
    private static string ApplyMappings(string rawPayload, Dictionary<string, string> mappings)
    {
        using var doc = JsonDocument.Parse(rawPayload);
        var result = new Dictionary<string, JsonElement>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var key = mappings.TryGetValue(prop.Name, out var canonical) ? canonical : prop.Name;
            result[key] = prop.Value.Clone();
        }

        return JsonSerializer.Serialize(result);
    }

    // Trích giá trị của businessKeyField từ canonicalPayload.
    private static string? ExtractBusinessKey(string? canonicalPayload, string businessKeyField)
    {
        if (string.IsNullOrEmpty(canonicalPayload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(canonicalPayload);
            if (doc.RootElement.TryGetProperty(businessKeyField, out var val))
                return val.ToString();
        }
        catch { }
        return null;
    }

    // SHA-256 của raw payload dưới dạng hex lowercase — dùng để phát hiện trùng lặp.
    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
