using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Providers.GetProviders;

// status = null → tất cả, "active"/"inactive" → filter.
public sealed record GetProvidersQuery(string? Status) : IRequest<Result<List<ProviderDto>>>;

public sealed class GetProvidersQueryHandler(
    IProviderRepository  providers,
    IOperationRepository operations)
    : IRequestHandler<GetProvidersQuery, Result<List<ProviderDto>>>
{
    public async Task<Result<List<ProviderDto>>> Handle(GetProvidersQuery request, CancellationToken ct)
    {
        ProviderStatus? statusFilter = request.Status?.ToLowerInvariant() switch
        {
            "active"   => ProviderStatus.Active,
            "inactive" => ProviderStatus.Inactive,
            _          => null
        };

        var list = await providers.GetAllAsync(statusFilter, ct);

        // Count operations cho từng provider (1 query/provider — chấp nhận được vì list nhỏ)
        var dtos = new List<ProviderDto>(list.Count);
        foreach (var p in list)
        {
            var ops = await operations.GetByProviderAsync(p.Code, ct);
            dtos.Add(new ProviderDto(
                p.Id, p.Code, p.DisplayName, p.BaseUrl,
                p.Status.ToString(), ops.Count, p.CreatedAtUtc));
        }

        return dtos;
    }
}
