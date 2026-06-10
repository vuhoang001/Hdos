using Hdos.DynamicFormService.Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Hdos.DynamicFormService.Infrastructure.Persistence;

public sealed class DynamicFormDbContext(DbContextOptions<DynamicFormDbContext> options) : DbContext(options)
{
    public DbSet<FormModule>       FormModules       => Set<FormModule>();
    public DbSet<FormTemplate>     FormTemplates     => Set<FormTemplate>();
    public DbSet<FormField>        FormFields        => Set<FormField>();
    public DbSet<FormSubmission>   FormSubmissions   => Set<FormSubmission>();
    public DbSet<FormScreen>       FormScreens       => Set<FormScreen>();
    public DbSet<FormScreenTab>    FormScreenTabs    => Set<FormScreenTab>();
    public DbSet<FormScreenWidget> FormScreenWidgets => Set<FormScreenWidget>();
    public DbSet<WidgetCatalog>    WidgetCatalogs    => Set<WidgetCatalog>();
    public DbSet<Provider>         Providers         => Set<Provider>();
    public DbSet<Operation>        Operations        => Set<Operation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DynamicFormDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        // Provider/Operation cho `lakehouse` từng được seed bằng HasData() ở migration
        // AddLakehouseSeed (commit doc 58). Phase 4 (doc 59) chuyển sang Lakehouse tự
        // push qua gRPC SyncRegistry khi startup → bỏ seed. Migration RemoveLakehouseSeed
        // xóa các row tĩnh đó trong DB.
        base.OnModelCreating(modelBuilder);
    }
}
