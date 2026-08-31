using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace FluentSpecifications.EntityFrameworkCore;

public sealed class RelationalSpecExecutor<T>
    where T : class
{
    private readonly DbContext _context;
    private readonly ISpecTranslator<T, Expression<Func<T, bool>>> _translator;

    public RelationalSpecExecutor(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _translator = new RelationalSpecTranslator<T>(context);
    }

    public async Task<IReadOnlyList<T>> ListAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = Prepare(specification);
        return await _context.Set<T>()
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> AnyAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = Prepare(specification);
        return _context.Set<T>().AnyAsync(predicate, cancellationToken);
    }

    public Task<int> CountAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = Prepare(specification);
        return _context.Set<T>().CountAsync(predicate, cancellationToken);
    }

    private Expression<Func<T, bool>> Prepare(Spec<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return _translator.Prepare(specification).GetPlanOrThrow();
    }
}
