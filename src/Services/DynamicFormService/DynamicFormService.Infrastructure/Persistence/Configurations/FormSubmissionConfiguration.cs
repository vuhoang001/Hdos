using Hdos.DynamicFormService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Configurations;

public sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> b)
    {
        b.ToTable("FormSubmissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.FormTemplateId).IsRequired();
        b.Property(x => x.ModuleCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.FormKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.FormVersion).IsRequired();
        b.Property(x => x.SubmittedBy);
        b.Property(x => x.Status).HasMaxLength(20).HasConversion<string>().IsRequired();
        b.Property(x => x.AnswersJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.SubmittedAt).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.FormTemplateId);
        b.HasIndex(x => new { x.ModuleCode, x.FormKey });
        b.HasIndex(x => x.SubmittedBy).HasFilter("\"SubmittedBy\" IS NOT NULL");
        b.HasIndex(x => x.SubmittedAt);

        b.Ignore(x => x.DomainEvents);
    }
}
