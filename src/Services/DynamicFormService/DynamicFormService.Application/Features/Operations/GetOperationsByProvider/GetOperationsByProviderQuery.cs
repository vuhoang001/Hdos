using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Operations.CreateOperation;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Operations.GetOperationsByProvider;

public sealed record GetOperationsByProviderQuery(string ProviderCode) : IRequest<Result<List<OperationDto>>>;

public sealed class GetOperationsByProviderQueryHandler(
    IProviderRepository  providers,
    IOperationRepository operations)
    : IRequestHandler<GetOperationsByProviderQuery, Result<List<OperationDto>>>
{
    public async Task<Result<List<OperationDto>>> Handle(GetOperationsByProviderQuery request, CancellationToken ct)
    {
        var providerCode = request.ProviderCode.Trim().ToLowerInvariant();

        if (!await providers.ExistsByCodeAsync(providerCode, ct))
            return Result.Failure<List<OperationDto>>(
                Error.NotFound($"Provider '{request.ProviderCode}' không tồn tại."));

        var ops = await operations.GetByProviderAsync(providerCode, ct);
        return ops.Select(OperationMapper.Map).ToList();
    }
}
