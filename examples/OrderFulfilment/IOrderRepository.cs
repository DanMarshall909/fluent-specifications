using FluentSpecifications;

namespace FluentSpecifications.Examples.OrderFulfilment;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(
        Spec<Order> specification,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        Spec<Order> specification,
        CancellationToken cancellationToken = default);

    Task<Page<Order>> FindAsync(
        PagedSearch<Order> search,
        CancellationToken cancellationToken = default);
}
