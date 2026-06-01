using Hdos.DynamicFormService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class DynamicFormDbContext(DbContextOptions<DynamicFormDbContext> options) : DbContext(options)
{
    public DbSet<FormModule>     FormModules     => Set<FormModule>();
    public DbSet<FormTemplate>   FormTemplates   => Set<FormTemplate>();
    public DbSet<FormField>      FormFields      => Set<FormField>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<FormPage>       FormPages       => Set<FormPage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DynamicFormDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        base.OnModelCreating(modelBuilder);
    }
}
