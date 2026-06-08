using FluentValidation;
using Hdos.LakehouseService.Application.DTOs;
using Hdos.LakehouseService.Domain.Entities;
using Hdos.LakehouseService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.LakehouseService.Application.Features.ViewBindings.CreateViewBinding;

public sealed record CreateViewBindingCommand(
    string  ViewName,
    string  SourceSystem,
    string  RecordType,
    string  BusinessKeyColumn,
    string? UpdatedAtColumn,
    int     PollIntervalSeconds) : IRequest<Result<ViewBindingDto>>;

public sealed class CreateViewBindingValidator : AbstractValidator<CreateViewBindingCommand>
{
    public CreateViewBindingValidator()
    {
        RuleFor(x => x.ViewName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SourceSystem).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RecordType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BusinessKeyColumn).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UpdatedAtColumn).MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.UpdatedAtColumn));
        RuleFor(x => x.PollIntervalSeconds).GreaterThanOrEqualTo(30);
    }
}

public sealed class CreateViewBindingHandler(IViewBindingRepository repo)
    : IRequestHandler<CreateViewBindingCommand, Result<ViewBindingDto>>
{
    public async Task<Result<ViewBindingDto>> Handle(CreateViewBindingCommand request, CancellationToken ct)
    {
        var existing = await repo.GetByViewNameAsync(request.ViewName, ct);
        if (existing is not null)
            return Result.Failure<ViewBindingDto>(
                Error.Conflict($"ViewBinding cho view '{request.ViewName}' đã tồn tại."));

        var entity = ViewBinding.Create(
            request.ViewName,
            request.SourceSystem,
            request.RecordType,
            request.BusinessKeyColumn,
            request.UpdatedAtColumn,
            request.PollIntervalSeconds);

        await repo.AddAsync(entity, ct);
        await repo.SaveChangesAsync(ct);

        return new ViewBindingDto(
            entity.Id, entity.ViewName, entity.SourceSystem, entity.RecordType,
            entity.BusinessKeyColumn, entity.UpdatedAtColumn, entity.PollIntervalSeconds,
            entity.IsActive, entity.CreatedAtUtc, entity.UpdatedAtUtc);
    }
}
