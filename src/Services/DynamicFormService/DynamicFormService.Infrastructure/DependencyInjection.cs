using Hdos.Common.Extensions;
using Hdos.DynamicFormService.Domain.Repositories;
using Hdos.DynamicFormService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hdos.DynamicFormService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDynamicFormInfrastructure(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.AddDbContext<DynamicFormDbContext>((sp, opts) =>
            opts.UseNpgsql(
                configuration.GetConnectionString("DynamicFormDb"),
                pg => pg.MigrationsAssembly(typeof(DynamicFormDbContext).Assembly.FullName)));

        services.AddScoped<IFormModuleRepository,     FormModuleRepository>();
        services.AddScoped<IFormTemplateRepository,   FormTemplateRepository>();
        services.AddScoped<IFormSubmissionRepository, FormSubmissionRepository>();
        services.AddScoped<IFormPageRepository,       FormPageRepository>();
        services.AddScoped<IDynamicFormUnitOfWork,    DynamicFormUnitOfWork>();

        services.AddMassTransitMessaging(configuration, x =>
        {
            x.AddEntityFrameworkOutbox<DynamicFormDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });
        }, servicePrefix: "df");

        return services;
    }
}
