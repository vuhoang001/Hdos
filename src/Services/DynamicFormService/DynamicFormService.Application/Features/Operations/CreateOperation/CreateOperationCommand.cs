using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Operations.CreateOperation;

public sealed record CreateOperationCommand(
    string       ProviderCode,
    string       OperationKey,
    string       DisplayName,
    string       Pattern,
    string?      SchemaPath,
    List<string> RequiredParams,
    string       Kind) : IRequest<Result<OperationDto>>;

public sealed class CreateOperationCommandValidator : AbstractValidator<CreateOperationCommand>
{
    public CreateOperationCommandValidator()
    {
        RuleFor(x => x.ProviderCode)
            .NotEmpty().MaximumLength(50)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("ProviderCode chỉ chữ thường, số, gạch ngang.");
        RuleFor(x => x.OperationKey)
            .NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("OperationKey chỉ chữ thường, số, gạch ngang.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Pattern).NotEmpty().MaximumLength(500);
        RuleFor(x => x.SchemaPath).MaximumLength(500).When(x => x.SchemaPath is not null);
        RuleFor(x => x.Kind)
            .Must(k => k.Equals("Single", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("List",   StringComparison.OrdinalIgnoreCase))
            .WithMessage("Kind phải là 'Single' hoặc 'List'.");
    }
}

public sealed class CreateOperationCommandHandler(
    IProviderRepository    providers,
    IOperationRepository   operations,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<CreateOperationCommand, Result<OperationDto>>
{
    public async Task<Result<OperationDto>> Handle(CreateOperationCommand request, CancellationToken ct)
    {
        var providerCode = request.ProviderCode.Trim().ToLowerInvariant();
        var operationKey = request.OperationKey.Trim().ToLowerInvariant();

        var provider = await providers.GetByCodeAsync(providerCode, ct);
        if (provider is null)
            return Result.Failure<OperationDto>(
                Error.NotFound($"Provider '{request.ProviderCode}' không tồn tại."));

        if (await operations.ExistsByKeyAsync(providerCode, operationKey, ct))
            return Result.Failure<OperationDto>(
                Error.Conflict($"Operation '{operationKey}' đã tồn tại trong provider '{providerCode}'."));

        var kind = Enum.Parse<OperationKind>(request.Kind, ignoreCase: true);

        var operation = Operation.Create(
            providerCode, operationKey, request.DisplayName,
            request.Pattern, request.SchemaPath, request.RequiredParams ?? new(), kind);

        await operations.AddAsync(operation, ct);
        await uow.SaveChangesAsync(ct);

        return OperationMapper.Map(operation);
    }
}

internal static class OperationMapper
{
    public static OperationDto Map(Operation op) => new(
        op.Id, op.ProviderCode, op.OperationKey, op.GetCombinedRef(),
        op.DisplayName, op.Pattern, op.SchemaPath, op.GetRequiredParams(),
        op.Kind.ToString(), op.Status.ToString(), op.CreatedAtUtc);
}
