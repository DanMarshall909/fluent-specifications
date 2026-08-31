using FluentSpecifications;

namespace FluentSpecifications.Examples.OrderFulfilment;

public sealed record QuickStartOrder(bool Paid, bool Priority);

[SpecificationSet<QuickStartOrder>]
public static partial class QuickStartRules
{
    public static Spec<QuickStartOrder> Paid =>
        Spec.Define<QuickStartOrder>(
            "order.paid",
            "Paid",
            order => order.Paid);

    public static Spec<QuickStartOrder> Priority =>
        Spec.Define<QuickStartOrder>(
            "order.priority",
            "Priority",
            order => order.Priority);
}

public static class QuickStartExample
{
    public static Spec<QuickStartOrder> Ready =>
        QuickStartRules.Paid.And.Priority;

    public static bool ShouldShip(QuickStartOrder order) =>
        Ready.Matches(order);
}
