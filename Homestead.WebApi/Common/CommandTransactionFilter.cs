namespace Homestead.WebApi.Common;

/// <summary>
/// Persistence/audit/transaction chokepoint for HTTP endpoints, applied once per module route group.
/// Every request whose verb can mutate (anything but GET/HEAD/OPTIONS) runs inside a single database
/// transaction via <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>: the endpoint runs, the work is
/// flushed and committed exactly once, and post-commit hooks drain afterwards. Any failure rolls the whole
/// request back. Read verbs run with no transaction.
/// </summary>
/// <remarks>
/// <para>
/// Read verbs must not write: nothing here enforces it, and a write made under one has no transaction. Each
/// explicit save autocommits on its own with no rollback, only <see cref="IUnitOfWork.SaveChangesAsync"/>
/// flushes pending audit entries, unsaved tracked changes are discarded, and after-commit hooks never run.
/// The known exception is <c>GET /api/auth/oidc/callback</c>,
/// whose user provisioning saves directly.
/// </para>
/// <para>
/// The transaction opens lazily on the first flush with work, so wrapping the whole endpoint (including a
/// POST that only reads) costs nothing, and a request rejected by authorization or validation before its
/// first write never opens one. Nesting is safe: an <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/> call
/// inside this filter joins the outer frame, which alone commits.
/// </para>
/// <para>
/// Apply it with <see cref="CommandTransactionEndpointExtensions.WithCommandTransaction{TBuilder}"/>, which
/// also adds the marker the coverage guard test checks for.
/// </para>
/// </remarks>
public sealed class CommandTransactionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return await next(context);

        var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
        return await unitOfWork.ExecuteInTransactionAsync(
            () => next(context).AsTask(), context.HttpContext.RequestAborted);
    }
}
