using Hdos.AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.AuthService.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        b.Property(p => p.Resource).HasMaxLength(80).IsRequired();
        b.Property(p => p.Action).HasMaxLength(80).IsRequired();
        b.Property(p => p.Description).HasMaxLength(255);
        b.Property(p => p.CreatedAtUtc).IsRequired();
        b.Property(p => p.UpdatedAtUtc);

        b.Ignore(p => p.Key);

        b.HasIndex(p => new { p.Resource, p.Action }).IsUnique();

        b.HasMany(p => p.RolePermissions)
            .WithOne(rp => rp.Permission)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
