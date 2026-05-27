using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Sources;

public sealed record GetSourcesQuery : IRequest<Result<List<SourceProfileDto>>>;

public sealed class GetSourcesHandler(ISourceProfileRepository repo)
    : IRequestHandler<GetSourcesQuery, Result<List<SourceProfileDto>>>
{
    public async Task<Result<List<SourceProfileDto>>> Handle(GetSourcesQuery request, CancellationToken ct)
    {
        var profiles = await repo.GetAllAsync(ct);

        var dtos = profiles
            .Select(p => new SourceProfileDto(
                p.Id,
                p.SourceSystem,
                p.DisplayName,
                p.BusinessKeyField,
                p.GetMappings()))
            .ToList();

        return dtos;
    }
}
