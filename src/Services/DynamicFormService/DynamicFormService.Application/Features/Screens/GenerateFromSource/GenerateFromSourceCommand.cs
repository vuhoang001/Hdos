using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Screens.GenerateFromSource;

// Khai báo một field sẽ được auto-generate.
// CanonicalKey != null → field bound vào expression {{sources.namespace.CanonicalKey}}, mặc định readOnly.
// CanonicalKey == null → field tự do, cần FieldKey tường minh.
public sealed record FieldGenerationInput(
    string?       CanonicalKey,
    string?       FieldKey,
    string        Label,
    string        FieldType    = "Text",
    bool?         IsReadOnly   = null,   // null = tự suy: true nếu bound, false nếu free
    string?       DisplayFormat = null,
    List<string>? Options       = null,
    bool          Required      = false);

public sealed record DataSourceConfig(
    string       Namespace,
    string       ServiceId,
    string       ResourcePath,
    List<string> RequiredParams,
    string?      SchemaPath = null);

// Một lệnh duy nhất tạo toàn bộ: Module (nếu chưa có) + Screen + DataSources + Form + Fields + Tab + Widget → publish
public sealed record GenerateFromSourceCommand(
    string                   ModuleCode,
    string?                  ModuleName,
    string                   ScreenCode,
    string                   ScreenTitle,
    string                   FormKey,
    string                   FormTitle,
    DataSourceConfig         DataSource,
    List<FieldGenerationInput> Fields) : IRequest<Result<GenerateFromSourceResultDto>>;

public sealed class GenerateFromSourceCommandValidator : AbstractValidator<GenerateFromSourceCommand>
{
    public GenerateFromSourceCommandValidator()
    {
        RuleFor(x => x.ModuleCode)
            .NotEmpty().MaximumLength(50)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("ModuleCode chỉ được chứa chữ thường, số và dấu gạch ngang.");
        RuleFor(x => x.ScreenCode)
            .NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("ScreenCode chỉ được chứa chữ thường, số và dấu gạch ngang.");
        RuleFor(x => x.FormKey)
            .NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9\-]+$").WithMessage("FormKey chỉ được chứa chữ thường, số và dấu gạch ngang.");
        RuleFor(x => x.ScreenTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FormTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DataSource.Namespace)
            .NotEmpty().MaximumLength(50)
            .Matches(@"^[a-z][a-z0-9_]*$").WithMessage("Namespace chỉ được chứa chữ thường, số và dấu gạch dưới.");
        RuleFor(x => x.DataSource.ServiceId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DataSource.ResourcePath).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Fields).NotEmpty().WithMessage("Phải có ít nhất 1 field.");
        RuleForEach(x => x.Fields).ChildRules(f =>
        {
            f.RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
            f.RuleFor(x => x.FieldKey)
                .NotEmpty().WithMessage("FieldKey bắt buộc khi CanonicalKey = null.")
                .Matches(@"^[a-z0-9_]+$").WithMessage("FieldKey chỉ được chứa chữ thường, số và dấu gạch dưới.")
                .When(x => x.CanonicalKey is null);
        });
    }
}

public sealed class GenerateFromSourceCommandHandler(
    IFormModuleRepository   modules,
    IFormScreenRepository   screens,
    IFormTemplateRepository templates,
    IDynamicFormUnitOfWork  uow)
    : IRequestHandler<GenerateFromSourceCommand, Result<GenerateFromSourceResultDto>>
{
    public async Task<Result<GenerateFromSourceResultDto>> Handle(
        GenerateFromSourceCommand request, CancellationToken ct)
    {
        // 1. Get or auto-create module
        var module = await modules.GetByCodeAsync(request.ModuleCode, ct);
        if (module is null)
        {
            module = FormModule.Create(
                request.ModuleCode,
                request.ModuleName ?? request.ModuleCode,
                null);
            await modules.AddAsync(module, ct);
        }

        // 2. Guard: screen & form không được tồn tại sẵn
        if (await screens.ExistsByCodeAsync(request.ModuleCode, request.ScreenCode, ct))
            return Result.Failure<GenerateFromSourceResultDto>(
                Error.Conflict($"Screen '{request.ScreenCode}' đã tồn tại trong module '{request.ModuleCode}'."));

        if (await templates.ExistsByKeyInModuleAsync(module.Id, request.FormKey, ct))
            return Result.Failure<GenerateFromSourceResultDto>(
                Error.Conflict($"Form '{request.FormKey}' đã tồn tại trong module '{request.ModuleCode}'."));

        // 3. Tạo FormTemplate + auto-add fields với expression binding
        var form = FormTemplate.Create(
            module.Id, module.Code, request.FormKey, request.FormTitle, null,
            new FormSettings("Gửi", "Đã gửi thành công", true));

        int order = 0;
        foreach (var fi in request.Fields)
        {
            var isBound  = fi.CanonicalKey is not null;
            var fieldKey = isBound ? fi.CanonicalKey!.ToLowerInvariant() : fi.FieldKey!;
            var binding  = isBound
                ? new DataBinding(
                    $"{{{{sources.{request.DataSource.Namespace}.{fi.CanonicalKey}}}}}",
                    fi.DisplayFormat)
                : null;
            var isReadOnly = fi.IsReadOnly ?? isBound;
            var fieldType  = Enum.TryParse<FieldType>(fi.FieldType, true, out var ft) ? ft : FieldType.Text;
            var options    = fi.Options?.Select(o => new FieldOption(o, o)).ToList();

            form.AddField(
                fieldKey, fi.Label, fieldType, order++, fi.Required,
                FieldWidth.Full, null, null, options, null, null,
                binding, isReadOnly);
        }

        form.Publish();
        await templates.AddAsync(form, ct);

        // 4. Tạo FormScreen + khai báo DataSource
        var screen = FormScreen.Create(
            module.Id, module.Code, request.ScreenCode, request.ScreenTitle, null);

        screen.SetDataSources(new List<DataSource>
        {
            new(request.DataSource.Namespace,
                request.DataSource.ServiceId,
                request.DataSource.ResourcePath,
                request.DataSource.RequiredParams,
                request.DataSource.SchemaPath)
        });

        // 5. Thêm tab mặc định + widget FormSection trỏ vào form vừa tạo
        var tab = screen.AddTab("Thông tin", "main", 0, true);
        var widget = FormScreenWidget.Create(
            tab.Id, "form-main", "FormSection",
            0, 0, 24, 12, "{}", form.Id);
        tab.ReplaceWidgets(new[] { widget });

        screen.Publish();
        screens.Add(screen);

        // 6. Lưu tất cả trong một transaction
        await uow.SaveChangesAsync(ct);

        return Result.Success(new GenerateFromSourceResultDto(
            module.Code,
            screen.Code,
            form.Key,
            form.Id,
            request.Fields.Count));
    }
}
