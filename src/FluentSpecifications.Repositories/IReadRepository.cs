namespace FluentSpecifications;

/// <summary>
/// Defines provider-neutral, materializing read operations over specifications and searches.
/// </summary>
/// <typeparam name="T">The candidate type queried by the repository.</typeparam>
public interface IReadRepository<T>
{
    /// <summary>
    /// Lists candidates matching <paramref name="specification"/>.
    /// </summary>
    Task<IReadOnlyList<T>> ListAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists candidates shaped by <paramref name="search"/>.
    /// </summary>
    Task<IReadOnlyList<T>> ListAsync(
        Search<T> search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a materialized page and its total-result metadata.
    /// </summary>
    Task<Page<T>> PageAsync(
        PagedSearch<T> search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether any candidate matches <paramref name="specification"/>.
    /// </summary>
    Task<bool> AnyAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether any candidate matches the filter in <paramref name="search"/>.
    /// </summary>
    Task<bool> AnyAsync(
        Search<T> search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts candidates matching <paramref name="specification"/>.
    /// </summary>
    Task<int> CountAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts candidates matching the filter in <paramref name="search"/>.
    /// </summary>
    Task<int> CountAsync(
        Search<T> search,
        CancellationToken cancellationToken = default);
}
