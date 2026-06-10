using FluentValidation;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Providers.SyncRegistry;

// Push-from-source upsert: service nguồn (Lakehouse...) gọi rpc này khi startup
// để DynamicForm tự cập nhật Provider + danh sách Operation. Idempotent.
//
// Operations cũ tồn tại trong DB cho provider này nhưng KHÔNG có trong payload
// → bị deactivate (giữ row để audit, không xóa cứng).
public sealed record SyncProviderRegistryCommand(
    string                          ProviderCode,
    string                          ProviderDisplayName,
    string                          ProviderBaseUrl,
    IReadOnlyList<OperationPayload> Operations) : IRequest<Result<SyncProviderRegistryResult>>;

public sealed record OperationPayload(
    string       OperationKey,
    string       DisplayName,
    string       Pattern,
    string?      SchemaPath,
    List<string> RequiredParams,
    string       Kind);

public sealed record SyncProviderRegistryResult(
    int UpsertedProviderCount,
    int UpsertedOperationCount,
    int DeactivatedOperationCount);

public sealed class SyncProviderRegistryCommandValidator : AbstractValidator<SyncProviderRegistryCommand>
{
    public SyncProviderRegistryCommandValidator()
    {
        RuleFor(x => x.ProviderCode)
            .NotEmpty().MaximumLength(50)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("ProviderCode chỉ chữ thường, số, gạch ngang.");
        RuleFor(x => x.ProviderDisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ProviderBaseUrl).NotEmpty().MaximumLength(500);
        RuleForEach(x => x.Operations).SetValidator(new OperationPayloadValidator());
    }
}

internal sealed class OperationPayloadValidator : AbstractValidator<OperationPayload>
{
    public OperationPayloadValidator()
    {
        RuleFor(x => x.OperationKey)
            .NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9\-]+$");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Pattern).NotEmpty().MaximumLength(500);
        RuleFor(x => x.SchemaPath).MaximumLength(500).When(x => x.SchemaPath is not null);
        RuleFor(x => x.Kind)
            .Must(k => k.Equals("Single", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("List",   StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class SyncProviderRegistryCommandHandler(
    IProviderRepository    providers,
    IOperationRepository   operations,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<SyncProviderRegistryCommand, Result<SyncProviderRegistryResult>>
{
    public async Task<Result<SyncProviderRegistryResult>> Handle(
        SyncProviderRegistryCommand request, CancellationToken ct)
    {
        var providerCode = request.ProviderCode.Trim().ToLowerInvariant();

        var provider = await providers.GetByCodeAsync(providerCode, ct);
        var upsertedProvider = 0;
        if (provider is null)
        {
            provider = Provider.Create(providerCode, request.ProviderDisplayName, request.ProviderBaseUrl);
            await providers.AddAsync(provider, ct);
            upsertedProvider = 1;
        }
        else
        {
            provider.Update(request.ProviderDisplayName, request.ProviderBaseUrl);
            if (provider.Status != ProviderStatus.Active) provider.Activate();
            upsertedProvider = 1;
        }

        var existing = await operations.GetByProviderAsync(providerCode, ct);
        var existingByKey = existing.ToDictionary(o => o.OperationKey, StringComparer.OrdinalIgnoreCase);
        var payloadKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var upsertedOps = 0;

        foreach (var payload in request.Operations)
        {
            var opKey = payload.OperationKey.Trim().ToLowerInvariant();
            payloadKeys.Add(opKey);
            var kind = Enum.Parse<OperationKind>(payload.Kind, ignoreCase: true);

            if (existingByKey.TryGetValue(opKey, out var op))
            {
                op.Update(payload.DisplayName, payload.Pattern, payload.SchemaPath,
                    payload.RequiredParams ?? new(), kind);
                if (op.Status != OperationStatus.Active) op.Activate();
            }
            else
            {
                op = Operation.Create(providerCode, opKey, payload.DisplayName,
                    payload.Pattern, payload.SchemaPath, payload.RequiredParams ?? new(), kind);
                await operations.AddAsync(op, ct);
            }
            upsertedOps++;
        }

        var deactivated = 0;
        foreach (var op in existing)
        {
            if (payloadKeys.Contains(op.OperationKey)) continue;
            if (op.Status == OperationStatus.Active)
            {
                op.Deactivate();
                deactivated++;
            }
        }

        await uow.SaveChangesAsync(ct);

        return new SyncProviderRegistryResult(upsertedProvider, upsertedOps, deactivated);
    }
}
