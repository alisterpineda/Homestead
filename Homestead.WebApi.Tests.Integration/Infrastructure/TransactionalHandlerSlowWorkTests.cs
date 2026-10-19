using System.Reflection;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Processing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Defense-in-depth (NOT the fix itself) for the lazy-transaction mechanism. Because the command
/// transaction is held while a handler runs, a handler that depends on a slow external service risks
/// re-pinning a pooled connection across that slow work. These tests find every handler that runs inside a
/// command transaction and fail if one takes a banned slow-work dependency. Two kinds run inside one:
/// <list type="bullet">
/// <item>handlers injected by an endpoint answering a verb other than GET/HEAD/OPTIONS — the verb is what
/// makes <c>CommandTransactionFilter</c> open the transaction, whatever the request record is named;</item>
/// <item>handlers a job handler (<see cref="IJobHandler"/>) injects, directly or through an
/// <c>I*Handler</c> interface, which it calls inside <c>ExecuteInTransactionAsync</c>. Found from the job
/// handlers' constructors, so where the interface is declared does not matter.</item>
/// </list>
/// <para>
/// The test sees the dependency <em>type</em>, not the method called — so it cannot tell whether a
/// classifier is used for the fast <c>ApplyAsync</c> or the slow <c>ClassifyAsync</c>. That is why the
/// classifier ban carries an explicit allowlist (see below). File streaming is also not type-detectable;
/// it stays covered by <c>Homestead.WebApi/CLAUDE.md</c> + code review.
/// </para>
/// </summary>
[Collection("Integration")]
public class TransactionalHandlerSlowWorkTests(PostgresFixture fixture)
{
    private static readonly Assembly WebApiAssembly = typeof(Program).Assembly;

    private static bool IsMutating(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is not { Count: > 0 } methods
        || methods.Any(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m) && !HttpMethods.IsOptions(m));

    private static bool IsHandler(Type t) =>
        t.Assembly == WebApiAssembly && t.Name.EndsWith("Handler", StringComparison.Ordinal);

    private static bool IsConcreteClass(Type t) => t is { IsClass: true, IsAbstract: false };

    private static bool IsJobHandler(Type t) => typeof(IJobHandler).IsAssignableFrom(t);

    private List<Type> TransactionalHandlers()
    {
        var endpointHandlers = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(IsMutating)
            .Select(e => e.Metadata.GetMetadata<MethodInfo>())
            .OfType<MethodInfo>()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Where(IsHandler);

        var webApiTypes = WebApiAssembly.GetTypes();
        var jobInvokedHandlers = webApiTypes
            .Where(t => IsConcreteClass(t) && IsJobHandler(t))
            .SelectMany(ConstructorParameterTypes)
            .Where(t => IsHandler(t) && !IsJobHandler(t))
            .SelectMany(t => t.IsInterface
                ? webApiTypes.Where(impl => IsConcreteClass(impl) && t.IsAssignableFrom(impl))
                : [t]);

        return endpointHandlers.Concat(jobInvokedHandlers).Distinct().ToList();
    }

    private static IEnumerable<Type> ConstructorParameterTypes(Type handler) =>
        handler.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

    [Fact]
    public void TransactionalHandlers_DiscoveryFindsTheTransactionalHandlers()
    {
        // The two guards below pass vacuously over an empty list, so pin the discovery itself.
        var handlers = TransactionalHandlers().Select(t => t.Name).ToHashSet();

        Assert.Contains("CreateGroupHandler", handlers);
        Assert.Contains("ApplyClassificationHandler", handlers);
        Assert.True(handlers.Count > 100, $"Expected > 100 transactional handlers, found {handlers.Count}.");
    }

    [Fact]
    public void TransactionalHandlers_DoNotDependOn_SlowWorkServices()
    {
        var banned = new[]
        {
            typeof(IAiClassificationService), typeof(IAiProvider), typeof(HttpClient), typeof(IHttpClientFactory),
        };
        // TestAiConnectionHandler calls IAiProvider behind a POST (so it counts as transactional) but never
        // writes, so the lazy command transaction never opens and no connection is held.
        var allowlist = new HashSet<string> { "TestAiConnectionHandler" };

        var violations = TransactionalHandlers()
            .Where(t => !allowlist.Contains(t.Name))
            .Where(t => ConstructorParameterTypes(t).Any(banned.Contains))
            .Select(t => t.Name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Transactional handlers must not inject a slow-work service (it would run inside the command " +
            $"transaction, re-pinning a connection). Offenders: {string.Join(", ", violations)}");
    }

    [Fact]
    public void TransactionalHandlers_DoNotDependOn_DocumentClassifier_ExceptAllowlist()
    {
        // These legitimately inject IDocumentClassifier:
        //  - ApplyClassificationHandler / CreateDocumentFromUploadHandler use ApplyAsync only (fast persist;
        //    the slow ClassifyAsync now runs upstream in the job scope).
        //  - SummarizeDocumentHandler still calls ClassifyAsync: the synchronous summarize endpoint is the one
        //    sanctioned slow synchronous handler, kept because IdleInTransactionTests uses it as the acceptance
        //    test that a slow AI call before the first flush holds no connection idle-in-transaction. The
        //    guardrail therefore does NOT protect the one handler most likely to regress — CLAUDE.md + code
        //    review are its cover.
        var allowlist = new HashSet<string>
        {
            "ApplyClassificationHandler",
            "CreateDocumentFromUploadHandler",
            "SummarizeDocumentHandler"
        };

        var violations = TransactionalHandlers()
            .Where(t => !allowlist.Contains(t.Name))
            .Where(t => ConstructorParameterTypes(t).Any(p => p == typeof(IDocumentClassifier)))
            .Select(t => t.Name)
            .ToList();

        Assert.True(violations.Count == 0,
            "Transactional handlers (outside the allowlist) must not inject IDocumentClassifier — the slow " +
            $"ClassifyAsync belongs in the job scope, not the command transaction. Offenders: {string.Join(", ", violations)}");
    }
}
