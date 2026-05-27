using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Sources;

// sourceSystem optional — nếu truyền vào thì chỉ lấy các loại của nguồn đó.
public sealed record GetSourcesQuery(string? SourceSystem) : IRequest<Result<List<SourceProfileDto>>>;

public sealed class GetSourcesHandler(ISourceProfileRepository repo)
    : IRequestHandler<GetSourcesQuery, Result<List<SourceProfileDto>>>
{
    public async Task<Result<List<SourceProfileDto>>> Handle(GetSourcesQuery request, CancellationToken ct)
    {
        var profiles = await repo.GetAllAsync(request.SourceSystem, ct);

        var dtos = profiles
            .Select(p => new SourceProfileDto(
                p.Id,
                p.SourceSystem,
                p.RecordType,
                p.DisplayName,
                p.BusinessKeyField,
                p.GetMappings()))
            .ToList();

        return dtos;
    }
}
