using FluentValidation;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.UpdateViewBinding;

public sealed record UpdateViewBindingCommand(
    Guid   Id,
    string ViewName,
    string SourceSystem,
    string RecordType,
    string BusinessKeyColumn,
    string UpdatedAtColumn,
    int    PollIntervalSeconds,
    bool   IsActive) : IRequest<Result<ViewBindingDto>>;

public sealed class UpdateViewBindingValidator : AbstractValidator<UpdateViewBindingCommand>
{
    public UpdateViewBindingValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ViewName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SourceSystem).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RecordType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BusinessKeyColumn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UpdatedAtColumn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PollIntervalSeconds).GreaterThanOrEqualTo(30);
    }
}

public sealed class UpdateViewBindingHandler(IViewBindingRepository repo)
    : IRequestHandler<UpdateViewBindingCommand, Result<ViewBindingDto>>
{
    public async Task<Result<ViewBindingDto>> Handle(UpdateViewBindingCommand request, CancellationToken ct)
    {
        var entity = await repo.GetByIdAsync(request.Id, ct);
        if (entity is null)
            return Result.Failure<ViewBindingDto>(Error.NotFound($"ViewBinding '{request.Id}'"));

        if (!string.Equals(entity.ViewName, request.ViewName, StringComparison.OrdinalIgnoreCase))
        {
            var conflict = await repo.GetByViewNameAsync(request.ViewName, ct);
            if (conflict is not null && conflict.Id != entity.Id)
                return Result.Failure<ViewBindingDto>(
                    Error.Conflict($"ViewBinding cho view '{request.ViewName}' đã tồn tại."));
        }

        entity.Update(
            request.ViewName,
            request.SourceSystem,
            request.RecordType,
            request.BusinessKeyColumn,
            request.UpdatedAtColumn,
            request.PollIntervalSeconds);
        entity.SetActive(request.IsActive);

        await repo.SaveChangesAsync(ct);

        return new ViewBindingDto(
            entity.Id, entity.ViewName, entity.SourceSystem, entity.RecordType,
            entity.BusinessKeyColumn, entity.UpdatedAtColumn, entity.PollIntervalSeconds,
            entity.IsActive, entity.CreatedAtUtc, entity.UpdatedAtUtc);
    }
}
