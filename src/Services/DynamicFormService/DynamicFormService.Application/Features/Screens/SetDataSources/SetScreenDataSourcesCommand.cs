using FluentValidation;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.SetDataSources;

// 2 mode khai báo (xem DataSource VO):
// - MANAGED: chỉ truyền OperationId (combined "providerCode::operationKey")
// - LEGACY:  truyền ServiceId + ResourcePath (+ SchemaPath optional)
public sealed record DataSourceInput(
    string       Namespace,
    List<string> RequiredParams,
    string?      OperationId  = null,
    string?      ServiceId    = null,
    string?      ResourcePath = null,
    string?      SchemaPath   = null);

public sealed record SetScreenDataSourcesCommand(
    string                ModuleCode,
    string                ScreenCode,
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

            d.RuleFor(x => x.OperationId)
                .MaximumLength(200)
                .Matches(@"^[a-z0-9\-]+::[a-z0-9\-]+$")
                .WithMessage("OperationId phải có dạng 'providerCode::operationKey'.")
                .When(x => !string.IsNullOrEmpty(x.OperationId));

            d.RuleFor(x => x.ServiceId).MaximumLength(50);
            d.RuleFor(x => x.ResourcePath).MaximumLength(300);
            d.RuleFor(x => x.SchemaPath).MaximumLength(300);

            // Phải có 1 trong 2 mode: OperationId (managed) HOẶC ServiceId+ResourcePath (legacy).
            d.RuleFor(x => x).Must(HasValidMode)
                .WithMessage("Mỗi DataSource phải có OperationId (managed) hoặc cả ServiceId + ResourcePath (legacy).");
        });

        RuleFor(x => x.DataSources)
            .Must(list => list.Select(d => d.Namespace).Distinct().Count() == list.Count)
            .WithMessage("Namespace phải là duy nhất trong danh sách.");
    }

    private static bool HasValidMode(DataSourceInput d)
    {
        var hasOperation = !string.IsNullOrWhiteSpace(d.OperationId);
        var hasLegacy    = !string.IsNullOrWhiteSpace(d.ServiceId)
                       && !string.IsNullOrWhiteSpace(d.ResourcePath);
        return hasOperation || hasLegacy;
    }
}

public sealed class SetScreenDataSourcesCommandHandler(
    IFormScreenRepository  screens,
    IOperationRepository   operations,
    IDynamicFormUnitOfWork uow)
    : IRequestHandler<SetScreenDataSourcesCommand, Result>
{
    public async Task<Result> Handle(SetScreenDataSourcesCommand request, CancellationToken ct)
    {
        var screen = await screens.GetByCodeAsync(request.ModuleCode, request.ScreenCode, ct);
        if (screen is null)
            return Result.Failure(Error.NotFound($"Screen '{request.ScreenCode}' trong module '{request.ModuleCode}'"));

        // Verify mọi OperationId tồn tại trong catalog trước khi lưu.
        foreach (var d in request.DataSources)
        {
            if (string.IsNullOrWhiteSpace(d.OperationId)) continue;

            var (providerCode, operationKey) = ParseOperationId(d.OperationId);
            var exists = await operations.ExistsByKeyAsync(providerCode, operationKey, ct);
            if (!exists)
                return Result.Failure(Error.NotFound(
                    $"Operation '{d.OperationId}' (ref bởi namespace '{d.Namespace}') không tồn tại trong catalog."));
        }

        var sources = request.DataSources
            .Select(d => new DataSource(
                d.Namespace,
                d.ServiceId,
                d.ResourcePath,
                d.RequiredParams ?? new(),
                d.SchemaPath,
                d.OperationId))
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

    private static (string providerCode, string operationKey) ParseOperationId(string combined)
    {
        var parts = combined.Split("::", 2);
        return (parts[0].Trim().ToLowerInvariant(), parts[1].Trim().ToLowerInvariant());
    }
}
