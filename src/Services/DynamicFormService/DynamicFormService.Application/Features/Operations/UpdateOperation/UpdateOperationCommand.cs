using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Operations.CreateOperation;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Operations.UpdateOperation;

public sealed record UpdateOperationCommand(
    string       ProviderCode,
    string       OperationKey,
    string       DisplayName,
    string       Pattern,
    string?      SchemaPath,
    List<string> RequiredParams,
    string       Kind,
    string?      Status) : IRequest<Result<OperationDto>>;

public sealed class UpdateOperationCommandValidator : AbstractValidator<UpdateOperationCommand>
{
    public UpdateOperationCommandValidator()
    {
        RuleFor(x => x.ProviderCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.OperationKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Pattern).NotEmpty().MaximumLength(500);
        RuleFor(x => x.SchemaPath).MaximumLength(500).When(x => x.SchemaPath is not null);
        RuleFor(x => x.Kind)
            .Must(k => k.Equals("Single", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("List",   StringComparison.OrdinalIgnoreCase))
            .WithMessage("Kind phải là 'Single' hoặc 'List'.");
        RuleFor(x => x.Status)
            .Must(s => s is null
                    || s.Equals("Active",   StringComparison.OrdinalIgnoreCase)
                    || s.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Status phải là 'Active' hoặc 'Inactive'.");
    }
}

public sealed class UpdateOperationCommandHandler(
    IOperationRepository   operations,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<UpdateOperationCommand, Result<OperationDto>>
{
    public async Task<Result<OperationDto>> Handle(UpdateOperationCommand request, CancellationToken ct)
    {
        var providerCode = request.ProviderCode.Trim().ToLowerInvariant();
        var operationKey = request.OperationKey.Trim().ToLowerInvariant();

        var op = await operations.GetByKeyAsync(providerCode, operationKey, ct);
        if (op is null)
            return Result.Failure<OperationDto>(
                Error.NotFound($"Operation '{providerCode}::{operationKey}' không tồn tại."));

        var kind = Enum.Parse<OperationKind>(request.Kind, ignoreCase: true);

        op.Update(request.DisplayName, request.Pattern, request.SchemaPath,
                  request.RequiredParams ?? new(), kind);

        if (request.Status is not null)
        {
            if (request.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                op.Activate();
            else
                op.Deactivate();
        }

        await uow.SaveChangesAsync(ct);

        return OperationMapper.Map(op);
    }
}
