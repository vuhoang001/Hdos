using FluentValidation;

namespace Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshots;

public sealed class GetSnapshotsQueryValidator : AbstractValidator<GetSnapshotsQuery>
{
    public GetSnapshotsQueryValidator()
    {
        RuleFor(x => x.Namespace).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Limit).InclusiveBetween(1, 1000);
    }
}
