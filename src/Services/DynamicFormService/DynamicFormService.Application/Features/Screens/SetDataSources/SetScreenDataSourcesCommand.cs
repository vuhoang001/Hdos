using FluentValidation;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.SetDataSources;

public sealed record DataSourceInput(
    string       Namespace,
    string       ServiceId,
    string       ResourcePath,
    List<string> RequiredParams,
    string?      SchemaPath = null);

public sealed record SetScreenDataSourcesCommand(
    string               ModuleCode,
    string               ScreenCode,
    List<DataSourceInput> DataSources) : IRequest<Result>;

public sealed class SetScreenDataSourcesCommandValidator : AbstractValidator<SetScreenDataSourcesCommand>
{
    public SetScreenDataSourcesCommandValidator()
    {
        RuleFor(x => x.ModuleCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScreenCode).NotEmpty().MaximumLength(100);
        RuleForEach(x => x.DataSources).ChildRules(d =>
        {
            d.RuleFor(x => x.Namespace)
                .NotEmpty().MaximumLength(50)
                .Matches(@"^[a-z][a-z0-9_]*$")
                .WithMessage("Namespace chỉ được chứa chữ thường, số và dấu gạch dưới, bắt đầu bằng chữ.");
            d.RuleFor(x => x.ServiceId).NotEmpty().MaximumLength(50);
            d.RuleFor(x => x.ResourcePath).NotEmpty().MaximumLength(300);
            d.RuleFor(x => x.SchemaPath).MaximumLength(300);
        });
        RuleFor(x => x.DataSources)
            .Must(list => list.Select(d => d.Namespace).Distinct().Count() == list.Count)
            .WithMessage("Namespace phải là duy nhất trong danh sách.");
    }
}

public sealed class SetScreenDataSourcesCommandHandler(
    IFormScreenRepository screens,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<SetScreenDataSourcesCommand, Result>
{
    public async Task<Result> Handle(SetScreenDataSourcesCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure(Error.NotFound($"Screen '{request.ScreenCode}' trong module '{request.ModuleCode}'"));

        var sources = request.DataSources
            .Select(d => new DataSource(d.Namespace, d.ServiceId, d.ResourcePath, d.RequiredParams, d.SchemaPath))
            .ToList();

        try
        {
            screen.SetDataSources(sources);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }

        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
