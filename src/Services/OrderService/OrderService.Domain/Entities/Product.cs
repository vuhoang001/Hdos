using Hdos.OrderService.Domain.Events;
using Hdos.SharedKernel;

namespace Hdos.OrderService.Domain.Entities;

public class Product : AggregateRoot<Guid>
{
    public Guid ProductId { get; private set; }
    public string? ProductName { get; private set; }
    public decimal ProductPrice { get; private set; }


    private Product()
    {
    }


    public static Product Create(Guid productId, string? productName, decimal productPrice)
    {
        if (productName is null)
        {
            throw new InvalidOperationException("ProductName is null");
        }

        var product = new Product
        {
            Id           = Guid.NewGuid(),
            ProductId    = productId,
            ProductName  = productName,
            ProductPrice = productPrice
        };

        product.RaiseDomainEvent(new ProductCreatedDomainEvent(productId, productName, productPrice));


        return product;
    }
}