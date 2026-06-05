using FluentAssertions;
using Hdos.DynamicFormService.Domain.Entities;
using Hdos.DynamicFormService.Domain.Enums;
using Xunit;

namespace Hdos.DynamicFormService.Tests.Domain;

public sealed class ProviderTests
{
    [Fact]
    public void Create_NormalizesCodeAndStripsTrailingSlash()
    {
        var p = Provider.Create("DataMatch", "Data Matching", "/dm/");

        p.Id.Should().NotBeEmpty();
        p.Code.Should().Be("datamatch");
        p.DisplayName.Should().Be("Data Matching");
        p.BaseUrl.Should().Be("/dm");
        p.Status.Should().Be(ProviderStatus.Active);
        p.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCode_Throws(string? code) =>
        FluentActions.Invoking(() => Provider.Create(code!, "DM", "/dm"))
            .Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_BlankDisplayName_Throws(string? name) =>
        FluentActions.Invoking(() => Provider.Create("dm", name!, "/dm"))
            .Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_BlankBaseUrl_Throws(string? url) =>
        FluentActions.Invoking(() => Provider.Create("dm", "DM", url!))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Update_AppliesValuesAndSetsUpdatedAt()
    {
        var p = Provider.Create("dm", "DM", "/dm");
        p.Update("DataMatching v2", "https://datamatch.local/");

        p.DisplayName.Should().Be("DataMatching v2");
        p.BaseUrl.Should().Be("https://datamatch.local");
        p.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void DeactivateAndActivate_ToggleStatus()
    {
        var p = Provider.Create("dm", "DM", "/dm");
        p.Status.Should().Be(ProviderStatus.Active);

        p.Deactivate();
        p.Status.Should().Be(ProviderStatus.Inactive);

        p.Activate();
        p.Status.Should().Be(ProviderStatus.Active);
    }
}
