using Hdos.DynamicFormService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.DynamicFormService.Infrastructure.Persistence.Configurations;

public sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> b)
    {
        b.ToTable("Operations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ProviderCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.OperationKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Pattern).HasMaxLength(500).IsRequired();
        b.Property(x => x.SchemaPath).HasMaxLength(500);
        b.Property(x => x.RequiredParamsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Kind).HasMaxLength(20).HasConversion<string>().IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).HasConversion<string>().IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        // Unique compound — DataSource resolve qua (ProviderCode, OperationKey).
        b.HasIndex(x => new { x.ProviderCode, x.OperationKey }).IsUnique();
        b.HasIndex(x => x.Status);
    }
}
