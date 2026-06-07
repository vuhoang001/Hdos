using FluentValidation;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Application.Helpers;
using Hdos.LakehouseService.Application.Services;
using Hdos.LakehouseService.Domain.Entities;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.CreateWithAutoProfile;

/// <summary>
/// MVP B — 1 call duy nhất gộp:
///   1. Introspect view metadata từ warehouse (information_schema.columns)
///   2. Sinh mappings convention (CanonicalNameSuggester)
///   3. Enroll SourceProfile sang DataMatching (HTTP, idempotent với 409)
///   4. Tạo ViewBinding cục bộ
///
/// Admin không control mapping — nếu cần đổi canonical name, gọi PUT /dm/sources/{id}
/// sau. Xem doc 45 §7.
/// </summary>
public sealed record CreateWithAutoProfileCommand(
    string ViewName,
    string SourceSystem,
    string RecordType,
    string BusinessKeyColumn,
    string UpdatedAtColumn,
    int    PollIntervalSeconds,
    string DisplayName) : IRequest<Result<CreateWithAutoProfileResultDto>>;

public sealed record CreateWithAutoProfileResultDto(
    ViewBindingDto             Binding,
    bool                       ProfileEnrolled,
    string                     BusinessKeyField,
    Dictionary<string, string> Mappings);

public sealed class CreateWithAutoProfileValidator : AbstractValidator<CreateWithAutoProfileCommand>
{
    public CreateWithAutoProfileValidator()
    {
        RuleFor(x => x.ViewName)
            .NotEmpty()
            .Matches(@"^[a-zA-Z_][\w]*\.[a-zA-Z_][\w]*$")
            .WithMessage("ViewName phải có dạng 'schema.view_name'.");
        RuleFor(x => x.SourceSystem).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RecordType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BusinessKeyColumn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UpdatedAtColumn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PollIntervalSeconds).GreaterThanOrEqualTo(30);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateWithAutoProfileHandler(
    IWarehouseSchemaIntrospector                  introspector,
    IViewBindingRepository                        bindings,
    ISourceProfileEnrollClient                    enrollClient,
    ILogger<CreateWithAutoProfileHandler>         logger)
    : IRequestHandler<CreateWithAutoProfileCommand, Result<CreateWithAutoProfileResultDto>>
{
    public async Task<Result<CreateWithAutoProfileResultDto>> Handle(
        CreateWithAutoProfileCommand cmd, CancellationToken ct)
    {
        // [1] Fail nhanh nếu binding cho view này đã tồn tại
        var existing = await bindings.GetByViewNameAsync(cmd.ViewName, ct);
        if (existing is not null)
            return Result.Failure<CreateWithAutoProfileResultDto>(
                Error.Conflict($"ViewBinding cho '{cmd.ViewName}' đã tồn tại."));

        // [2] Introspect schema view
        var columns = await introspector.GetColumnsAsync(cmd.ViewName, ct);
        if (columns.Count == 0)
            return Result.Failure<CreateWithAutoProfileResultDto>(
                Error.NotFound($"View '{cmd.ViewName}' hoặc hdos_reader thiếu quyền SELECT"));

        var columnNames = columns.Select(c => c.Name).ToList();

        // [3] Verify businessKeyColumn + updatedAtColumn nằm trong columns
        if (!columnNames.Contains(cmd.BusinessKeyColumn, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CreateWithAutoProfileResultDto>(
                Error.Validation($"Cột '{cmd.BusinessKeyColumn}' không có trong view '{cmd.ViewName}'."));
        if (!columnNames.Contains(cmd.UpdatedAtColumn, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CreateWithAutoProfileResultDto>(
                Error.Validation($"Cột '{cmd.UpdatedAtColumn}' không có trong view '{cmd.ViewName}'."));

        // [4] Sinh mappings convention
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var col in columnNames)
            mappings[col] = CanonicalNameSuggester.Suggest(col);

        // BusinessKeyField = canonical name của businessKeyColumn
        var businessKeyField = mappings[cmd.BusinessKeyColumn];

        // [5] Enroll SourceProfile bên DataMatching (idempotent với 409)
        var enroll = await enrollClient.EnrollAsync(new SourceProfileEnrollRequest(
            SourceSystem:     cmd.SourceSystem,
            RecordType:       cmd.RecordType,
            DisplayName:      cmd.DisplayName,
            BusinessKeyField: businessKeyField,
            Mappings:         mappings), ct);

        if (enroll.IsFailure)
            return Result.Failure<CreateWithAutoProfileResultDto>(enroll.Error);

        // [6] Tạo ViewBinding cục bộ
        var binding = ViewBinding.Create(
            cmd.ViewName,
            cmd.SourceSystem,
            cmd.RecordType,
            cmd.BusinessKeyColumn,
            cmd.UpdatedAtColumn,
            cmd.PollIntervalSeconds);

        await bindings.AddAsync(binding, ct);
        await bindings.SaveChangesAsync(ct);

        logger.LogInformation(
            "Auto-enrolled ViewBinding {View} ({Src}/{Type}) + SourceProfile với {N} mappings",
            binding.ViewName, binding.SourceSystem, binding.RecordType, mappings.Count);

        var bindingDto = new ViewBindingDto(
            binding.Id, binding.ViewName, binding.SourceSystem, binding.RecordType,
            binding.BusinessKeyColumn, binding.UpdatedAtColumn, binding.PollIntervalSeconds,
            binding.IsActive, binding.CreatedAtUtc, binding.UpdatedAtUtc);

        return new CreateWithAutoProfileResultDto(
            Binding:          bindingDto,
            ProfileEnrolled:  true,
            BusinessKeyField: businessKeyField,
            Mappings:         mappings);
    }
}
