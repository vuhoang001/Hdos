using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.UpdateScreen;

public sealed record UpdateScreenCommand(
    string  ModuleCode,
    string  ScreenCode,
    string  Title,
    string? Description,
    int     SortOrder) : IRequest<Result<FormScreenDto>>;

public sealed class UpdateScreenCommandValidator : AbstractValidator<UpdateScreenCommand>
{
    public UpdateScreenCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}

public sealed class UpdateScreenCommandHandler(
    IFormScreenRepository  screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<UpdateScreenCommand, Result<FormScreenDto>>
{
    public async Task<Result<FormScreenDto>> Handle(UpdateScreenCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure<FormScreenDto>(Error.NotFound($"Screen '{request.ScreenCode}'"));

        screen.Update(request.Title, request.Description, request.SortOrder);
        await uow.SaveChangesAsync(ct);

        return ScreenMapper.ToDto(screen, screen.Tabs.Count);
    }
}
