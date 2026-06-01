using System.Text.Json;
using Hdos.DynamicFormService.Domain.Enums;
using Hdos.DynamicFormService.Domain.Events;
using Hdos.DynamicFormService.Domain.ValueObjects;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

public sealed class FormSubmission : AggregateRoot<Guid>
{
    public Guid             FormTemplateId { get; private set; }
    public string           ModuleCode     { get; private set; } = null!;
    public string           FormKey        { get; private set; } = null!;
    public int              FormVersion    { get; private set; }
    public Guid?            SubmittedBy    { get; private set; }
    public SubmissionStatus Status         { get; private set; }
    public string           AnswersJson    { get; private set; } = null!;  // jsonb: List<FieldAnswer>
    public DateTime         SubmittedAt    { get; private set; }

    private FormSubmission() { }

    public static FormSubmission Create(
        Guid               formTemplateId,
        string             moduleCode,
        string             formKey,
        int                formVersion,
        Guid?              submittedBy,
        List<FieldAnswer>  answers)
    {
        var s = new FormSubmission
        {
            Id             = Guid.NewGuid(),
            FormTemplateId = formTemplateId,
            ModuleCode     = moduleCode,
            FormKey        = formKey,
            FormVersion    = formVersion,
            SubmittedBy    = submittedBy,
            Status         = SubmissionStatus.Submitted,
            AnswersJson    = JsonSerializer.Serialize(answers),
            SubmittedAt    = DateTime.UtcNow
        };
        s.RaiseDomainEvent(new FormSubmittedDomainEvent(
            s.Id, s.FormTemplateId, s.ModuleCode, s.FormKey, s.SubmittedBy));
        return s;
    }

    public void MarkReviewed()
    {
        Status       = SubmissionStatus.Reviewed;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
