using Hdos.DynamicFormService.Domain.Entities;

namespace Hdos.DynamicFormService.Domain.Repositories;

public interface IFormSubmissionRepository
{
    Task<List<FormSubmission>> GetByFormTemplateAsync(Guid formTemplateId, int page, int pageSize, CancellationToken ct);
    Task<int> CountByFormTemplateAsync(Guid formTemplateId, CancellationToken ct);
    Task AddAsync(FormSubmission submission, CancellationToken ct);
}
