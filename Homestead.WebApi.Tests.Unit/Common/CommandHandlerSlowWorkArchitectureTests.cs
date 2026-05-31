using System.Reflection;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Services;
using MediatR;

namespace Homestead.WebApi.Tests.Unit.Common;

/// <summary>
/// Defense-in-depth (NOT the fix itself) for the lazy-transaction mechanism. Because the command
/// transaction is held while a handler runs, a handler that depends on a slow external service risks
/// re-pinning a pooled connection across that slow work. These tests reflect over every
/// <c>ICommand</c> handler in the WebApi assembly and fail if one takes a banned slow-work dependency.
/// <para>
/// The test sees the dependency <em>type</em>, not the method called — so it cannot tell whether a
/// classifier is used for the fast <c>ApplyAsync</c> or the slow <c>ClassifyAsync</c>. That is why the
/// classifier ban carries an explicit allowlist (see below). File streaming is also not type-detectable;
/// it stays covered by <c>Homestead.WebApi/CLAUDE.md</c> + code review.
/// </para>
/// </summary>
public class CommandHandlerSlowWorkArchitectureTests
{
    private static readonly Assembly WebApiAssembly = typeof(IUnitOfWork).Assembly;

    private static List<(Type Handler, Type RequestType)> CommandHandlers()
    {
        return WebApiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => (Handler: t, RequestType: i.GetGenericArguments()[0])))
            // Only mutation handlers run inside ExecuteInTransactionAsync. Queries (ClassifyPreviewQuery,
            // etc.) are plain IRequest<> — never ICommand — so they hold no transaction and are exempt.
            // ClassifyPreviewHandler injects IDocumentClassifier but is a query, so it is intentionally
            // not in scope here (this is by design, not an oversight).
            .Where(x => typeof(ICommand).IsAssignableFrom(x.RequestType))
            .ToList();
    }

    private static IEnumerable<Type> ConstructorParameterTypes(Type handler) =>
        handler.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

    [Fact]
    public void CommandHandlers_DoNotDependOn_SlowWorkServices()
    {
        var banned = new[] { typeof(IAiClassificationService), typeof(HttpClient), typeof(IHttpClientFactory) };

        var violations = CommandHandlers()
            .Where(x => ConstructorParameterTypes(x.Handler).Any(banned.Contains))
            .Select(x => x.Handler.Name)
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0,
            "ICommand handlers must not inject a slow-work service (it would run inside the command " +
            $"transaction, re-pinning a connection). Offenders: {string.Join(", ", violations)}");
    }

    [Fact]
    public void CommandHandlers_DoNotDependOn_DocumentClassifier_ExceptAllowlist()
    {
        // These three legitimately inject IDocumentClassifier:
        //  - ApplyClassificationHandler / CreateDocumentFromUploadHandler use ApplyAsync only (fast persist;
        //    the slow ClassifyAsync now runs upstream in the job scope).
        //  - SummarizeDocumentHandler still calls ClassifyAsync because Summarize stays synchronous. The
        //    guardrail therefore does NOT protect the one handler most likely to regress — CLAUDE.md + code
        //    review are its cover. This is the accepted cost of keeping Summarize synchronous.
        var allowlist = new HashSet<string>
        {
            "ApplyClassificationHandler",
            "CreateDocumentFromUploadHandler",
            "SummarizeDocumentHandler"
        };

        var violations = CommandHandlers()
            .Where(x => !allowlist.Contains(x.Handler.Name))
            .Where(x => ConstructorParameterTypes(x.Handler).Any(t => t == typeof(IDocumentClassifier)))
            .Select(x => x.Handler.Name)
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0,
            "ICommand handlers (outside the allowlist) must not inject IDocumentClassifier — the slow " +
            $"ClassifyAsync belongs in the job scope, not the command transaction. Offenders: {string.Join(", ", violations)}");
    }
}
