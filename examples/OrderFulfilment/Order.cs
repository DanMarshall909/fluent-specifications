namespace FluentSpecifications.Examples.OrderFulfilment;

public sealed class Order
{
    public int Id { get; set; }

    public bool Paid { get; set; }

    public bool HasDeliveryAddress { get; set; }

    public bool HighPriority { get; set; }

    public bool Suspended { get; set; }

    public bool ManualOverride { get; set; }

    public bool Archived { get; set; }

    public int TotalCents { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? CustomerReference { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTimeOffset ProviderTimestamp { get; set; }

    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public bool HasCustomerName(string expected) =>
        string.Equals(CustomerName, expected, StringComparison.OrdinalIgnoreCase);
}

public sealed class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<Order> Orders { get; set; } = [];
}
