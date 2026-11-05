using System.Linq.Expressions;

namespace Homestead.WebApi.Common.Search;

/// <summary>
/// Combines entity predicates that are each written once as a lambda (so EF translates them as written) into one
/// predicate, by rebinding every operand onto a single parameter. Lets a search define each match branch once
/// and reuse it for the filter, the ordering and the per-row "matched on" report.
/// </summary>
public static class PredicateBuilder
{
    public static Expression<Func<T, bool>> Or<T>(params Expression<Func<T, bool>>[] predicates) =>
        Combine(predicates, Expression.OrElse, fallback: false);

    public static Expression<Func<T, bool>> And<T>(params Expression<Func<T, bool>>[] predicates) =>
        Combine(predicates, Expression.AndAlso, fallback: true);

    /// <summary>
    /// <paramref name="predicate"/> applied to <paramref name="selector"/>'s value: <c>x =&gt; predicate(selector(x))</c>,
    /// inlined (no Invoke node), so EF translates it as if written by hand.
    /// </summary>
    public static Expression<Func<T, bool>> Compose<T, TValue>(
        Expression<Func<T, TValue>> selector, Expression<Func<TValue, bool>> predicate)
    {
        var body = new ParameterReplacer(predicate.Parameters[0], selector.Body).Visit(predicate.Body);
        return Expression.Lambda<Func<T, bool>>(body, selector.Parameters[0]);
    }

    /// <summary>
    /// <c>x =&gt; collection(x).Any(predicate)</c> with the predicate inlined as the lambda the C# compiler would
    /// have emitted, so EF translates the collection navigation as usual.
    /// </summary>
    public static Expression<Func<T, bool>> Any<T, TItem>(
        Expression<Func<T, IEnumerable<TItem>>> collection, Expression<Func<TItem, bool>> predicate)
    {
        var any = typeof(Enumerable).GetMethods()
            .Single(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TItem));
        return Expression.Lambda<Func<T, bool>>(
            Expression.Call(any, collection.Body, predicate), collection.Parameters[0]);
    }

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>>[] predicates, Func<Expression, Expression, BinaryExpression> join, bool fallback)
    {
        if (predicates.Length == 0)
            return _ => fallback;

        var parameter = Expression.Parameter(typeof(T), "d");
        Expression body = Rebind(predicates[0], parameter);
        foreach (var predicate in predicates.Skip(1))
            body = join(body, Rebind(predicate, parameter));

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression Rebind<T>(Expression<Func<T, bool>> predicate, ParameterExpression parameter) =>
        new ParameterReplacer(predicate.Parameters[0], parameter).Visit(predicate.Body);

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
