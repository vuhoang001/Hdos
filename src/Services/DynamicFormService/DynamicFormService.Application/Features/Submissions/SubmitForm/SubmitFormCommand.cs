using FluentValidation;
using Hdos.Common.Messaging;
using Hdos.Contracts.IntegrationEvents;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Submissions.SubmitForm;

public sealed record SubmitFormCommand(
    string                    ModuleCode,
    string                    FormKey,
    Guid?                     SubmittedBy,
    List<FieldAnswerInputDto> Answers) : IRequest<Result<SubmitFormResultDto>>;

public sealed class SubmitFormCommandValidator : AbstractValidator<SubmitFormCommand>
{
    public SubmitFormCommandValidator()
    {
        RuleFor(x => x.ModuleCode).NotEmpty();
        RuleFor(x => x.FormKey).NotEmpty();
        RuleFor(x => x.Answers).NotNull();
        RuleForEach(x => x.Answers).ChildRules(a => a.RuleFor(i => i.FieldKey).NotEmpty());
    }
}

public sealed class SubmitFormCommandHandler(
    IFormTemplateRepository   templates,
    IFormSubmissionRepository submissions,
    IDynamicFormUnitOfWork    uow,
    IEventBus                 eventBus)
    : IRequestHandler<SubmitFormCommand, Result<SubmitFormResultDto>>
{
    public async Task<Result<SubmitFormResultDto>> Handle(SubmitFormCommand request, CancellationToken ct)
    {
        var template = await templates.GetByModuleAndKeyAsync(
            request.ModuleCode, request.FormKey, includeFields: false, ct);

        if (template is null)
            return Result.Failure<SubmitFormResultDto>(
                Error.NotFound($"Form '{request.FormKey}' trong module '{request.ModuleCode}'"));

        if (template.Status != Domain.Enums.FormStatus.Published)
            return Result.Failure<SubmitFormResultDto>(Error.Conflict("Form chưa được publish."));

        if (!template.Status.Equals(Domain.Enums.FormStatus.Published))
            return Result.Failure<SubmitFormResultDto>(Error.Conflict("Form không khả dụng."));

        var answers = request.Answers
            .Select(a => new FieldAnswer(a.FieldKey, a.Value))
            .ToList();

        var submission = FormSubmission.Create(
            template.Id, template.ModuleCode, template.Key, template.Version,
            request.SubmittedBy, answers);

        await submissions.AddAsync(submission, ct);
        await uow.SaveChangesAsync(ct);

        await eventBus.PublishAsync(new FormSubmittedIntegrationEvent(
            submission.Id, template.Id, template.ModuleCode, template.Key, request.SubmittedBy), ct);

        return new SubmitFormResultDto(submission.Id);
    }
}
