using Hdos.AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.AuthService.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();

        b.Property(r => r.Name).HasMaxLength(80).IsRequired();
        b.Property(r => r.Description).HasMaxLength(255);
        b.Property(r => r.CreatedAtUtc).IsRequired();
        b.Property(r => r.UpdatedAtUtc);

        b.HasIndex(r => r.Name).IsUnique();

        b.HasMany(r => r.RolePermissions)
            .WithOne(rp => rp.Role)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
