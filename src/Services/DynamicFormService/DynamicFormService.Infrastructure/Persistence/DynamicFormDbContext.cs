using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
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

        SeedLakehouseProvider(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    // Bridge Tier-1 → Tier-2: đăng ký Lakehouse như 1 Provider trong catalog của DynamicForm
    // để admin chọn được "lakehouse::prefill" / "lakehouse::chart" khi tạo DataSource cho screen.
    // Xem docs/58-lakehouse-dynamicform-integration.md.
    private static void SeedLakehouseProvider(ModelBuilder modelBuilder)
    {
        var seedAtUtc = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Provider>().HasData(new
        {
            Id           = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Code         = "lakehouse",
            DisplayName  = "Lakehouse Data Contracts",
            BaseUrl      = "http://lakehouseservice:8080",
            Status       = ProviderStatus.Active,
            CreatedAtUtc = seedAtUtc,
            UpdatedAtUtc = (DateTime?)null
        });

        modelBuilder.Entity<Operation>().HasData(
            new
            {
                Id                 = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ProviderCode       = "lakehouse",
                OperationKey       = "prefill",
                DisplayName        = "Form Prefill Consumer",
                Pattern            = "/lakehouse/contracts/{contractCode}/prefill",
                SchemaPath         = (string?)"/lakehouse/contracts/{contractCode}/schema",
                RequiredParamsJson = "[\"contractCode\"]",
                Kind               = OperationKind.Single,
                Status             = OperationStatus.Active,
                CreatedAtUtc       = seedAtUtc,
                UpdatedAtUtc       = (DateTime?)null
            },
            new
            {
                Id                 = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                ProviderCode       = "lakehouse",
                OperationKey       = "chart",
                DisplayName        = "SDUI Chart Page Consumer",
                Pattern            = "/lakehouse/contracts/{contractCode}/chart",
                SchemaPath         = (string?)null,
                RequiredParamsJson = "[\"contractCode\"]",
                Kind               = OperationKind.Single,
                Status             = OperationStatus.Active,
                CreatedAtUtc       = seedAtUtc,
                UpdatedAtUtc       = (DateTime?)null
            });
    }
}
