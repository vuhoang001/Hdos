using FluentAssertions;
using Hdos.Contracts.DataContracts.Finance;
using Xunit;

namespace Hdos.BuildingBlocks.Tests.Contracts.DataContracts;

public sealed class FinanceDailyValidatorTests
{
    private readonly FinanceDailyValidator _v = new();

    private static FinanceDailyRow Valid() => new(
        InvoiceDate:            new DateOnly(2026, 6, 9),
        DepartmentId:           1,
        DepartmentName:         "Khoa Tim mạch",
        TotalInvoiceAmount:     1_000_000m,
        TotalDiscountAmount:    100_000m,
        InvoiceCount:           10,
        DistinctEncounterCount: 8,
        FinanceBucket:          "BHYT");

    [Fact]
    public async Task Validates_valid_row()
    {
        var r = await _v.ValidateAsync(Valid(), default);
        r.IsValid.Should().BeTrue();
        r.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_non_positive_department_id()
    {
        var r = await _v.ValidateAsync(Valid() with { DepartmentId = 0 }, default);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("DepartmentId"));
    }

    [Fact]
    public async Task Rejects_empty_department_name()
    {
        var r = await _v.ValidateAsync(Valid() with { DepartmentName = "" }, default);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("DepartmentName"));
    }

    [Fact]
    public async Task Rejects_discount_exceeding_total()
    {
        var r = await _v.ValidateAsync(
            Valid() with { TotalInvoiceAmount = 100m, TotalDiscountAmount = 200m },
            default);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("Discount"));
    }

    [Fact]
    public async Task Rejects_negative_amounts()
    {
        var r = await _v.ValidateAsync(Valid() with { TotalInvoiceAmount = -1m }, default);
        r.IsValid.Should().BeFalse();

        r = await _v.ValidateAsync(Valid() with { InvoiceCount = -5 }, default);
        r.IsValid.Should().BeFalse();
    }
}
