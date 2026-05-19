using Hdos.AuthService.Domain.Entities;
using Hdos.AuthService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.AuthService.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.Id);

        b.Property(u => u.Id).ValueGeneratedNever();

        b.Property(u => u.FullName).HasMaxLength(120).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(u => u.CreatedAtUtc).IsRequired();
        b.Property(u => u.UpdatedAtUtc);
        b.Property(u => u.LastSeenUtc);

        b.Property(u => u.Email)
            .HasConversion(
                e => e.Value,
                v => Email.Create(v).Value!)
            .HasMaxLength(255)
            .IsRequired();

        b.HasIndex(u => u.Email).IsUnique();

        b.Ignore(u => u.DomainEvents);
    }
}
