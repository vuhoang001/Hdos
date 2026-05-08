using FluentValidation.TestHelper;
using Hdos.OrderService.Application.DTOs;
using Hdos.OrderService.Application.Features.CreateOrder;
using Xunit;

namespace Hdos.OrderService.Tests.Validators;

public sealed class CreateOrderCommandValidatorTests
{
    private readonly CreateOrderCommandValidator _v = new();

    private static OrderItemInputDto Item(string p = "Book", int q = 1, decimal u = 10m, string c = "USD") =>
        new(p, q, u, c);

    [Fact]
    public void EmptyCustomerId_Fails()
    {
        _v.TestValidate(new CreateOrderCommand(Guid.Empty, "a@b.io", new[] { Item() }))
            .ShouldHaveValidationErrorFor(x => x.CustomerId);
    }

    [Fact]
    public void EmptyItems_Fails()
    {
        _v.TestValidate(new CreateOrderCommand(Guid.NewGuid(), "a@b.io", Array.Empty<OrderItemInputDto>()))
            .ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Item_BadQuantity_Fails()
    {
        _v.TestValidate(new CreateOrderCommand(Guid.NewGuid(), "a@b.io", new[] { Item(q: 0) }))
            .ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Item_NegativeUnitPrice_Fails()
    {
        _v.TestValidate(new CreateOrderCommand(Guid.NewGuid(), "a@b.io", new[] { Item(u: -1m) }))
            .ShouldHaveValidationErrorFor("Items[0].UnitPrice");
    }

    [Fact]
    public void Item_BadCurrency_Fails()
    {
        _v.TestValidate(new CreateOrderCommand(Guid.NewGuid(), "a@b.io", new[] { Item(c: "USDD") }))
            .ShouldHaveValidationErrorFor("Items[0].Currency");
    }

    [Fact]
    public void Item_EmptyProductName_Fails()
    {
        _v.TestValidate(new CreateOrderCommand(Guid.NewGuid(), "a@b.io", new[] { Item(p: "") }))
            .ShouldHaveValidationErrorFor("Items[0].ProductName");
    }

    [Fact]
    public void Valid_NoErrors()
    {
        _v.TestValidate(new CreateOrderCommand(Guid.NewGuid(), "a@b.io", new[] { Item() }))
            .ShouldNotHaveAnyValidationErrors();
    }
}
