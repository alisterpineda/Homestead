using System.Linq.Expressions;

namespace Homestead.WebApi.Common.Search;

/// <summary>
/// Combines entity predicates that are each written once as a lambda (so EF translates them as written) into one
/// OR'd predicate, by rebinding every operand onto a single parameter. Lets a search define each match branch once
/// and reuse it for the filter, the ordering and the per-row "matched on" report.
/// </summary>
public static class PredicateBuilder
{
    public static Expression<Func<T, bool>> Or<T>(params Expression<Func<T, bool>>[] predicates)
    {
        if (predicates.Length == 0)
            return _ => false;

        var parameter = Expression.Parameter(typeof(T), "d");
        Expression body = Rebind(predicates[0], parameter);
        foreach (var predicate in predicates.Skip(1))
            body = Expression.OrElse(body, Rebind(predicate, parameter));

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression Rebind<T>(Expression<Func<T, bool>> predicate, ParameterExpression parameter) =>
        new ParameterReplacer(predicate.Parameters[0], parameter).Visit(predicate.Body);

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
