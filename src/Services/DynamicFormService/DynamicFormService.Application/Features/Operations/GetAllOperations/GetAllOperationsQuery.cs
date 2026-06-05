using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Application.Features.Operations.CreateOperation;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Operations.GetAllOperations;

// Cross-provider list — FE dùng để render ProviderOperationSelect dropdown.
public sealed record GetAllOperationsQuery(string? Status) : IRequest<Result<List<OperationDto>>>;

public sealed class GetAllOperationsQueryHandler(IOperationRepository operations)
    : IRequestHandler<GetAllOperationsQuery, Result<List<OperationDto>>>
{
    public async Task<Result<List<OperationDto>>> Handle(GetAllOperationsQuery request, CancellationToken ct)
    {
        OperationStatus? statusFilter = request.Status?.ToLowerInvariant() switch
        {
            "active"   => OperationStatus.Active,
            "inactive" => OperationStatus.Inactive,
            _          => null
        };

        var ops = await operations.GetAllAsync(statusFilter, ct);
        return ops.Select(OperationMapper.Map).ToList();
    }
}
