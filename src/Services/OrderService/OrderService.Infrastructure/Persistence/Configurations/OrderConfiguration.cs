using Hdos.OrderService.Domain.Entities;
using Hdos.OrderService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.OrderService.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Orders");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();

        b.Property(o => o.CustomerId).IsRequired();
        b.Property(o => o.CustomerEmail).HasMaxLength(255).IsRequired();
        b.Property(o => o.Status).HasConversion<int>().IsRequired();
        b.Property(o => o.CreatedAtUtc).IsRequired();
        b.Property(o => o.UpdatedAtUtc);

        b.OwnsOne(o => o.Total, money =>
        {
            money.Property(m => m.Amount).HasColumnName("TotalAmount").HasColumnType("decimal(18,2)");
            money.Property(m => m.Currency).HasColumnName("TotalCurrency").HasMaxLength(3);
        });

        b.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(o => o.CustomerId);
        b.Ignore(o => o.DomainEvents);

        b.Navigation(o => o.Items).Metadata.SetField("_items");
        b.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("OrderItems");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();

        b.Property(i => i.ProductName).HasMaxLength(120).IsRequired();
        b.Property(i => i.Quantity).IsRequired();
        b.Property(i => i.OrderId).IsRequired();

        b.OwnsOne(i => i.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("UnitPriceAmount").HasColumnType("decimal(18,2)");
            money.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3);
        });

        b.Ignore(i => i.LineTotal);
    }
}
