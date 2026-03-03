using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Homestead.WebApi.Tests.Unit.Infrastructure;

/// <summary>
/// Wraps an in-memory collection as an IQueryable that supports EF Core async operations.
/// </summary>
internal static class TestAsyncQueryable
{
    public static IQueryable<T> AsAsyncQueryable<T>(this IEnumerable<T> source)
    {
        return new AsyncQueryableWrapper<T>(source.AsQueryable());
    }

    private sealed class AsyncQueryableWrapper<T>(IQueryable<T> inner)
        : IOrderedQueryable<T>, IAsyncEnumerable<T>, IQueryProvider, IAsyncQueryProvider
    {
        public Type ElementType => inner.ElementType;
        public Expression Expression => inner.Expression;
        public IQueryProvider Provider => this;

        public IQueryable CreateQuery(Expression expression) =>
            new AsyncQueryableWrapper<T>((IQueryable<T>)inner.Provider.CreateQuery(expression));

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new AsyncQueryableWrapper<TElement>(inner.Provider.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => inner.Provider.Execute(expression);
        public TResult Execute<TResult>(Expression expression) => inner.Provider.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken ct = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executeMethod = typeof(IQueryProvider).GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!;
            var result = executeMethod.MakeGenericMethod(resultType).Invoke(inner.Provider, [expression]);
            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType).Invoke(null, [result])!;
        }

        public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
        {
            foreach (var item in inner)
            {
                yield return item;
                await Task.CompletedTask;
            }
        }
    }
}
