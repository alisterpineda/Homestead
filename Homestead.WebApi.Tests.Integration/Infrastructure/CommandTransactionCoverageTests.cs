using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Each module maps its own top-level route group, so each group needs its own
/// <c>.WithCommandTransaction()</c>. Forgetting it compiles fine and, once nothing else opens a transaction,
/// lets that group's mutations autocommit piece by piece with no audit flush. Every API endpoint that answers
/// a mutating verb must carry <see cref="CommandTransactionMetadata"/>.
/// </summary>
[Collection("Integration")]
public class CommandTransactionCoverageTests(PostgresFixture fixture)
{
    private static bool IsMutating(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is not { Count: > 0 } methods
        || methods.Any(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m) && !HttpMethods.IsOptions(m));

    [Fact]
    public void MutatingApiEndpoints_RunInACommandTransaction()
    {
        var mutating = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .Where(IsMutating)
            .ToList();

        // Guards the guard: if the route filter stopped matching, the assertion below would pass vacuously.
        Assert.True(mutating.Count > 100, $"Expected > 100 mutating API endpoints, found {mutating.Count}.");

        var uncovered = mutating
            .Where(e => e.Metadata.GetMetadata<CommandTransactionMetadata>() is null)
            .Select(e => e.DisplayName)
            .ToList();

        Assert.True(uncovered.Count == 0,
            "Mutating API endpoints outside a .WithCommandTransaction() group: " + string.Join(", ", uncovered));
    }
}
