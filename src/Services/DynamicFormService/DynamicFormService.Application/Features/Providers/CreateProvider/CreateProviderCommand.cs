using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Providers.CreateProvider;

public sealed record CreateProviderCommand(
    string Code,
    string DisplayName,
    string BaseUrl) : IRequest<Result<ProviderDto>>;

public sealed class CreateProviderCommandValidator : AbstractValidator<CreateProviderCommand>
{
    public CreateProviderCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(@"^[a-z0-9\-]+$")
            .WithMessage("Code chỉ được chứa chữ thường, số và dấu gạch ngang.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(500);
    }
}

public sealed class CreateProviderCommandHandler(
    IProviderRepository    providers,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<CreateProviderCommand, Result<ProviderDto>>
{
    public async Task<Result<ProviderDto>> Handle(CreateProviderCommand request, CancellationToken ct)
    {
        var exists = await providers.ExistsByCodeAsync(request.Code.Trim().ToLowerInvariant(), ct);
        if (exists)
            return Result.Failure<ProviderDto>(
                Error.Conflict($"Provider với code '{request.Code}' đã tồn tại."));

        var provider = Provider.Create(request.Code, request.DisplayName, request.BaseUrl);
        await providers.AddAsync(provider, ct);
        await uow.SaveChangesAsync(ct);

        return new ProviderDto(
            provider.Id,
            provider.Code,
            provider.DisplayName,
            provider.BaseUrl,
            provider.Status.ToString(),
            0,
            provider.CreatedAtUtc);
    }
}
