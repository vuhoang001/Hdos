using Hdos.AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.AuthService.Infrastructure.Persistence.Configurations;

public sealed class UserLicenseConfiguration : IEntityTypeConfiguration<UserLicense>
{
    public void Configure(EntityTypeBuilder<UserLicense> b)
    {
        b.ToTable("UserLicenses");
        b.HasKey(l => l.Id);

        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.UserId).IsRequired();
        b.Property(l => l.Plan).HasMaxLength(50).IsRequired();
        b.Property(l => l.ModulesCsv).HasMaxLength(500).IsRequired();
        b.Property(l => l.ExpiresAtUtc);
        b.Property(l => l.IsActive).IsRequired();
        b.Property(l => l.CreatedAtUtc).IsRequired();
        b.Property(l => l.UpdatedAtUtc);

        b.HasIndex(l => new { l.UserId, l.IsActive });
    }
}
