using FluentValidation;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Operations.DeleteOperation;

public sealed record DeleteOperationCommand(
    string ProviderCode,
    string OperationKey) : IRequest<Result>;

public sealed class DeleteOperationCommandValidator : AbstractValidator<DeleteOperationCommand>
{
    public DeleteOperationCommandValidator()
    {
        RuleFor(x => x.ProviderCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.OperationKey).NotEmpty().MaximumLength(100);
    }
}

// Lưu ý: handler hiện không validate DataSource đang ref Operation.
// Để giữ atomicity với screen, FE nên hiển thị warning "có N screens đang dùng"
// trước khi gọi DELETE. Sau khi xóa, screen render sẽ skip DataSource đó.
public sealed class DeleteOperationCommandHandler(
    IOperationRepository   operations,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<DeleteOperationCommand, Result>
{
    public async Task<Result> Handle(DeleteOperationCommand request, CancellationToken ct)
    {
        var providerCode = request.ProviderCode.Trim().ToLowerInvariant();
        var operationKey = request.OperationKey.Trim().ToLowerInvariant();

        var op = await operations.GetByKeyAsync(providerCode, operationKey, ct);
        if (op is null)
            return Result.Failure(
                Error.NotFound($"Operation '{providerCode}::{operationKey}' không tồn tại."));

        operations.Remove(op);
        await uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
