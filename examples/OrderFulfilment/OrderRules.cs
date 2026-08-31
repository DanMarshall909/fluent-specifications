using FluentSpecifications;

namespace FluentSpecifications.Examples.OrderFulfilment;

[SpecificationSet<Order>(GenerateSearch = true)]
public static partial class OrderRules
{
    public static Spec<Order> Paid =>
        Spec.Define<Order>("order.paid", "Paid", order => order.Paid);

    public static Spec<Order> HasDeliveryAddress =>
        Spec.Define<Order>(
            "order.has-delivery-address",
            "Has delivery address",
            order => order.HasDeliveryAddress);

    public static Spec<Order> HighPriority =>
        Spec.Define<Order>(
            "order.high-priority",
            "High priority",
            order => order.HighPriority);

    public static Spec<Order> Suspended =>
        Spec.Define<Order>(
            "order.suspended",
            "Suspended",
            order => order.Suspended);

    public static Spec<Order> ManualOverride =>
        Spec.Define<Order>(
            "order.manual-override",
            "Manual override",
            order => order.ManualOverride);

    public static Spec<Order> WorthAtLeast(int minimumCents) =>
        Spec.Define<Order>(
            "order.worth-at-least",
            "Worth at least",
            order => order.TotalCents >= minimumCents);

    public static Spec<Order> CustomerReferenceIsNot(string blockedReference) =>
        Spec.Define<Order>(
            "order.customer-reference-is-not",
            "Customer reference is not blocked",
            order => order.CustomerReference != blockedReference);

    public static Spec<Order> CustomerNamedExactly(string expected) =>
        Spec.Define<Order>(
            "order.customer-named-exactly",
            "Customer named exactly",
            order => order.CustomerName == expected);

    public static Spec<Order> CustomerNamedIgnoringCase(string expected) =>
        Spec.Define<Order>(
            "order.customer-named-ignoring-case",
            "Customer named ignoring case",
            order => string.Equals(
                order.CustomerName,
                expected,
                StringComparison.OrdinalIgnoreCase));

    public static Spec<Order> CustomerNamedByDomainMethod(string expected) =>
        Spec.Define<Order>(
            "order.customer-named-by-domain-method",
            "Customer named by domain method",
            order => order.HasCustomerName(expected));

    public static Spec<Order> HasCustomerNamed(string expected) =>
        Spec.Define<Order>(
            "order.has-customer-named",
            "Has customer named",
            order => order.Customer != null && order.Customer.Name == expected);

    public static Spec<Order> UnsafeCustomerNamed(string expected) =>
        Spec.Define<Order>(
            "order.unsafe-customer-named",
            "Unsafe customer named",
            order => order.Customer!.Name == expected);

    public static Spec<Order> CreatedBefore(DateTime cutoff) =>
        Spec.Define<Order>(
            "order.created-before",
            "Created before",
            order => order.CreatedAt < cutoff);

    public static Spec<Order> ProviderTimestampBefore(DateTimeOffset cutoff) =>
        Spec.Define<Order>(
            "order.provider-timestamp-before",
            "Provider timestamp before",
            order => order.ProviderTimestamp < cutoff);

    [Expose]
    public static Spec<Order> CanShip =>
        Paid
            .And(HasDeliveryAddress)
            .AndNot(Suspended)
            .Named(
                "order.can-ship",
                "Can ship",
                "The order is not ready to ship.");
}
