namespace FluentSpecifications.EntityFrameworkCore;

/// <summary>
/// Implements the provider-neutral read repository with Entity Framework Core.
/// </summary>
/// <typeparam name="T">The mapped entity type queried by the repository.</typeparam>
public sealed class EntityFrameworkRepository<T> : IReadRepository<T>
    where T : class
{
    private readonly RelationalSpecExecutor<T> _executor;

    public EntityFrameworkRepository(Microsoft.EntityFrameworkCore.DbContext context)
    {
        _executor = new RelationalSpecExecutor<T>(context);
    }

    public Task<IReadOnlyList<T>> ListAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default) =>
        _executor.ListAsync(specification, cancellationToken);

    public Task<IReadOnlyList<T>> ListAsync(
        Search<T> search,
        CancellationToken cancellationToken = default) =>
        _executor.ListAsync(search, cancellationToken);

    public Task<Page<T>> PageAsync(
        PagedSearch<T> search,
        CancellationToken cancellationToken = default) =>
        _executor.PageAsync(search, cancellationToken);

    public Task<bool> AnyAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default) =>
        _executor.AnyAsync(specification, cancellationToken);

    public Task<bool> AnyAsync(
        Search<T> search,
        CancellationToken cancellationToken = default) =>
        _executor.AnyAsync(search, cancellationToken);

    public Task<int> CountAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default) =>
        _executor.CountAsync(specification, cancellationToken);

    public Task<int> CountAsync(
        Search<T> search,
        CancellationToken cancellationToken = default) =>
        _executor.CountAsync(search, cancellationToken);
}
