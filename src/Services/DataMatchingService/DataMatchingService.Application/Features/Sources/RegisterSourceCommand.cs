using FluentValidation;
using Hdos.DataMatchingService.Application.DTOs;
using Hdos.DataMatchingService.Domain.Entities;
using Hdos.DataMatchingService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DataMatchingService.Application.Features.Sources;

public sealed record RegisterSourceCommand(
    string SourceSystem,
    string DisplayName,
    string BusinessKeyField,
    Dictionary<string, string> Mappings) : IRequest<Result<SourceProfileDto>>;

public sealed class RegisterSourceValidator : AbstractValidator<RegisterSourceCommand>
{
    public RegisterSourceValidator()
    {
        RuleFor(x => x.SourceSystem).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BusinessKeyField).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Mappings).NotEmpty();
        RuleFor(x => x.BusinessKeyField)
            .Must((cmd, field) => cmd.Mappings.ContainsValue(field))
            .WithMessage("BusinessKeyField must be one of the canonical (target) values in Mappings.");
    }
}

public sealed class RegisterSourceHandler(
    ISourceProfileRepository repo,
    IDataMatchingUnitOfWork uow)
    : IRequestHandler<RegisterSourceCommand, Result<SourceProfileDto>>
{
    public async Task<Result<SourceProfileDto>> Handle(RegisterSourceCommand request, CancellationToken ct)
    {
        var existing = await repo.GetBySystemAsync(request.SourceSystem, ct);
        if (existing is not null)
            return Result.Failure<SourceProfileDto>(
                Error.Conflict($"SourceSystem '{request.SourceSystem}' is already registered."));

        var profile = SourceProfile.Create(
            request.SourceSystem,
            request.DisplayName,
            request.BusinessKeyField,
            request.Mappings);

        await repo.AddAsync(profile, ct);
        await uow.SaveChangesAsync(ct);

        return new SourceProfileDto(
            profile.Id,
            profile.SourceSystem,
            profile.DisplayName,
            profile.BusinessKeyField,
            profile.GetMappings());
    }
}
