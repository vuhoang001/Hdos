using Hdos.NotificationService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.NotificationService.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(n => n.Id);
        b.Property(n => n.Id).ValueGeneratedNever();

        b.Property(n => n.Recipient).HasMaxLength(255).IsRequired();
        b.Property(n => n.Subject).HasMaxLength(200).IsRequired();
        b.Property(n => n.Body).HasMaxLength(4000).IsRequired();
        b.Property(n => n.Channel).HasConversion<int>().IsRequired();
        b.Property(n => n.Status).HasConversion<int>().IsRequired();
        b.Property(n => n.FailureReason).HasMaxLength(500);
        b.Property(n => n.CreatedAtUtc).IsRequired();
        b.Property(n => n.SentAtUtc);
        b.Property(n => n.UpdatedAtUtc);

        b.HasIndex(n => n.Recipient);
        b.HasIndex(n => n.CreatedAtUtc);
        b.Ignore(n => n.DomainEvents);
    }
}
