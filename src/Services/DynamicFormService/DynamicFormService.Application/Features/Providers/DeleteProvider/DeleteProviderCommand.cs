using FluentValidation;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Providers.DeleteProvider;

public sealed record DeleteProviderCommand(string Code) : IRequest<Result>;

public sealed class DeleteProviderCommandValidator : AbstractValidator<DeleteProviderCommand>
{
    public DeleteProviderCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
    }
}

public sealed class DeleteProviderCommandHandler(
    IProviderRepository    providers,
    IOperationRepository   operations,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<DeleteProviderCommand, Result>
{
    public async Task<Result> Handle(DeleteProviderCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToLowerInvariant();

        var provider = await providers.GetByCodeAsync(code, ct);
        if (provider is null)
            return Result.Failure(Error.NotFound($"Provider '{request.Code}' không tồn tại."));

        // Rule: không cho xóa Provider khi còn Operation tham chiếu.
        // Admin phải xóa Operations trước (hoặc deactivate Provider thay vì xóa).
        if (await operations.AnyByProviderAsync(code, ct))
            return Result.Failure(Error.Conflict(
                $"Provider '{request.Code}' còn Operations đang dùng. Xóa Operations trước hoặc Deactivate."));

        providers.Remove(provider);
        await uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
