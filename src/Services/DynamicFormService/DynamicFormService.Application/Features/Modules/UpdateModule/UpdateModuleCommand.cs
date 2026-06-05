using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Modules.UpdateModule;

public sealed record UpdateModuleCommand(
    string  Code,
    string  Name,
    string? Description) : IRequest<Result<FormModuleDto>>;

public sealed class UpdateModuleCommandValidator : AbstractValidator<UpdateModuleCommand>
{
    public UpdateModuleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}

public sealed class UpdateModuleCommandHandler(
    IFormModuleRepository  modules,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<UpdateModuleCommand, Result<FormModuleDto>>
{
    public async Task<Result<FormModuleDto>> Handle(UpdateModuleCommand request, CancellationToken ct)
    {
        var module = await modules.GetByCodeAsync(request.Code, ct);
        if (module is null)
            return Result.Failure<FormModuleDto>(Error.NotFound($"Module '{request.Code}'"));

        module.Update(request.Name, request.Description);
        await uow.SaveChangesAsync(ct);

        return Map(module);
    }

    private static FormModuleDto Map(FormModule m) => new(
        m.Id, m.Code, m.Name, m.Description, m.Status.ToString(), 0, 0, [], m.CreatedAtUtc);
}
