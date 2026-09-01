using FluentSpecifications;
using static FluentSpecifications.Examples.OrderFulfilment.OrderRules;

namespace FluentSpecifications.Examples.OrderFulfilment;

public static class ShippingExamples
{
    public static Spec<Order> ReadyToShip() =>
        CanShip.And.HighPriority.AndNot.Suspended;

    public static Spec<Order> PriorityOrManuallyApproved() =>
        CanShip.And(HighPriority.Or.ManualOverride);

    public static Spec<Order> ValuableOrder(int minimumCents) =>
        CanShip.And.WorthAtLeast(minimumCents);

    public static PagedSearch<Order> PriorityShippingPage()
    {
        var request = Order.Search
            .Matching.CanShip.And.HighPriority
            .Sorted.By.CreatedAt.Desc
            .Then.By.Id.Asc
            .Page(2).OfSize(50);

        return request;
    }

    public static bool ShouldDispatch(Order order)
    {
        if (order.CanShip)
        {
            return true;
        }

        return false;
    }

    public static CheckResult ExplainWhyShippingIsBlocked(Order order) =>
        CanShip.Check(order);

    public static Task<IReadOnlyList<Order>> FindReadyOrdersAsync(
        IReadRepository<Order> repository,
        CancellationToken cancellationToken = default) =>
        repository.ListAsync(
            CanShip.And(HighPriority.Or.ManualOverride),
            cancellationToken);
}
