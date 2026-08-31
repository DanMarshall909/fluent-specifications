using FluentSpecifications;
using static FluentSpecifications.NuGet.Tests.NuGetOrderRules;
using Xunit;

namespace FluentSpecifications.NuGet.Tests;

public sealed class PublishedPackageTests
{
    [Fact]
    public void Published_1_0_0_package_remains_a_supported_upgrade_baseline()
    {
        var rule = CanShip.And.Priority;
        var priorityOrder = new NuGetOrder(
            Paid: true,
            HasAddress: true,
            Priority: true);

        Assert.True(rule.Matches(priorityOrder));
        Assert.True(priorityOrder.CanShip);
        Assert.False(rule.Matches(new NuGetOrder(Paid: true, HasAddress: true)));
    }
}

public sealed record NuGetOrder(
    bool Paid = false,
    bool HasAddress = false,
    bool Priority = false,
    bool Suspended = false);

[SpecificationSet<NuGetOrder>]
public static partial class NuGetOrderRules
{
    public static Spec<NuGetOrder> Paid =>
        Spec.Define<NuGetOrder>("order.paid", "Paid", order => order.Paid);

    public static Spec<NuGetOrder> HasAddress =>
        Spec.Define<NuGetOrder>(
            "order.has-address",
            "Has address",
            order => order.HasAddress);

    public static Spec<NuGetOrder> Priority =>
        Spec.Define<NuGetOrder>("order.priority", "Priority", order => order.Priority);

    public static Spec<NuGetOrder> Suspended =>
        Spec.Define<NuGetOrder>("order.suspended", "Suspended", order => order.Suspended);

    [Expose]
    public static Spec<NuGetOrder> CanShip =>
        Paid
            .And(HasAddress)
            .AndNot(Suspended)
            .Named("order.can-ship", "Can ship");
}
