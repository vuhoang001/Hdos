using Hdos.AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.AuthService.Infrastructure.Persistence.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles");
        b.HasKey(ur => ur.Id);
        b.Property(ur => ur.Id).ValueGeneratedNever();

        b.Property(ur => ur.UserId).IsRequired();
        b.Property(ur => ur.RoleId).IsRequired();
        b.Property(ur => ur.CreatedAtUtc).IsRequired();
        b.Property(ur => ur.UpdatedAtUtc);

        b.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();

        b.HasOne(ur => ur.Role)
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
