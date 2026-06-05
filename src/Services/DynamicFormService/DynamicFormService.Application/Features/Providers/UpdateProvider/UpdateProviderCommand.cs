using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Providers.UpdateProvider;

public sealed record UpdateProviderCommand(
    string  Code,
    string  DisplayName,
    string  BaseUrl,
    string? Status) : IRequest<Result<ProviderDto>>;

public sealed class UpdateProviderCommandValidator : AbstractValidator<UpdateProviderCommand>
{
    public UpdateProviderCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Status)
            .Must(s => s is null || s.Equals("Active", StringComparison.OrdinalIgnoreCase) || s.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Status phải là 'Active' hoặc 'Inactive'.");
    }
}

public sealed class UpdateProviderCommandHandler(
    IProviderRepository    providers,
    IOperationRepository   operations,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<UpdateProviderCommand, Result<ProviderDto>>
{
    public async Task<Result<ProviderDto>> Handle(UpdateProviderCommand request, CancellationToken ct)
    {
        var provider = await providers.GetByCodeAsync(request.Code.Trim().ToLowerInvariant(), ct);
        if (provider is null)
            return Result.Failure<ProviderDto>(
                Error.NotFound($"Provider '{request.Code}' không tồn tại."));

        provider.Update(request.DisplayName, request.BaseUrl);

        if (request.Status is not null)
        {
            if (request.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                provider.Activate();
            else
                provider.Deactivate();
        }

        await uow.SaveChangesAsync(ct);

        var operationCount = (await operations.GetByProviderAsync(provider.Code, ct)).Count;

        return new ProviderDto(
            provider.Id,
            provider.Code,
            provider.DisplayName,
            provider.BaseUrl,
            provider.Status.ToString(),
            operationCount,
            provider.CreatedAtUtc);
    }
}
