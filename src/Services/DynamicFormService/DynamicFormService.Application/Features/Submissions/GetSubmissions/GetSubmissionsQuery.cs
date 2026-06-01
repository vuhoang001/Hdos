using System.Text.Json;
using FluentValidation;
using Hdos.DynamicFormService.Application.DTOs;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.Submissions.GetSubmissions;

public sealed record GetSubmissionsQuery(
    Guid FormTemplateId,
    int  Page     = 1,
    int  PageSize = 20) : IRequest<Result<List<FormSubmissionDto>>>;

public sealed class GetSubmissionsQueryValidator : AbstractValidator<GetSubmissionsQuery>
{
    public GetSubmissionsQueryValidator()
    {
        RuleFor(x => x.FormTemplateId).NotEmpty();
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetSubmissionsQueryHandler(IFormSubmissionRepository submissions)
    : IRequestHandler<GetSubmissionsQuery, Result<List<FormSubmissionDto>>>
{
    public async Task<Result<List<FormSubmissionDto>>> Handle(GetSubmissionsQuery request, CancellationToken ct)
    {
        var list = await submissions.GetByFormTemplateAsync(request.FormTemplateId, request.Page, request.PageSize, ct);

        return list.Select(s => new FormSubmissionDto(
            s.Id, s.ModuleCode, s.FormKey, s.FormVersion, s.SubmittedBy,
            s.Status.ToString(), s.SubmittedAt,
            JsonSerializer.Deserialize<object>(s.AnswersJson))).ToList();
    }
}
