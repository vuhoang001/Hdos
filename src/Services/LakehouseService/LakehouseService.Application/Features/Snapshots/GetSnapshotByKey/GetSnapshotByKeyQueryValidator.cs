using FluentValidation;

namespace Hdos.LakehouseService.Application.Features.Snapshots.GetSnapshotByKey;

public sealed class GetSnapshotByKeyQueryValidator : AbstractValidator<GetSnapshotByKeyQuery>
{
    public GetSnapshotByKeyQueryValidator()
    {
        RuleFor(x => x.Namespace).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Key).NotEmpty().MaximumLength(500);
    }
}
