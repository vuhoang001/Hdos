using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Providers.GetProviderByCode;

public sealed record GetProviderByCodeQuery(string Code) : IRequest<Result<ProviderDto>>;

public sealed class GetProviderByCodeQueryHandler(
    IProviderRepository  providers,
    IOperationRepository operations)
    : IRequestHandler<GetProviderByCodeQuery, Result<ProviderDto>>
{
    public async Task<Result<ProviderDto>> Handle(GetProviderByCodeQuery request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToLowerInvariant();

        var provider = await providers.GetByCodeAsync(code, ct);
        if (provider is null)
            return Result.Failure<ProviderDto>(
                Error.NotFound($"Provider '{request.Code}' không tồn tại."));

        var ops = await operations.GetByProviderAsync(code, ct);

        return new ProviderDto(
            provider.Id, provider.Code, provider.DisplayName, provider.BaseUrl,
            provider.Status.ToString(), ops.Count, provider.CreatedAtUtc);
    }
}
