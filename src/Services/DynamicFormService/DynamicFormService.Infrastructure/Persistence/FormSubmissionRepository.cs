using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class FormSubmissionRepository(DynamicFormDbContext db) : IFormSubmissionRepository
{
    public Task<List<FormSubmission>> GetByFormTemplateAsync(Guid formTemplateId, int page, int pageSize, CancellationToken ct)
        => db.FormSubmissions
            .Where(s => s.FormTemplateId == formTemplateId)
            .OrderByDescending(s => s.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public Task<int> CountByFormTemplateAsync(Guid formTemplateId, CancellationToken ct)
        => db.FormSubmissions.CountAsync(s => s.FormTemplateId == formTemplateId, ct);

    public async Task AddAsync(FormSubmission submission, CancellationToken ct)
        => await db.FormSubmissions.AddAsync(submission, ct);
}
