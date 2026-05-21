using Hdos.OrderService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hdos.OrderService.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        b.Property(p => p.ProductId).IsRequired();
        b.Property(p => p.ProductName).HasMaxLength(120).IsRequired();
        b.Property(p => p.ProductPrice).HasColumnType("decimal(18,2)").IsRequired();

        b.HasIndex(p => p.ProductId).IsUnique();
        b.Ignore(p => p.DomainEvents);
    }
}
