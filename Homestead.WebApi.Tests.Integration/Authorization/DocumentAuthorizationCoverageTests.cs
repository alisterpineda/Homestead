using Homestead.WebApi.Authorization;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Authorization;

/// <summary>
/// Document-level authorization lives on the endpoint (<c>.RequireDocumentAuthorization(...)</c>), so
/// forgetting it on a new endpoint compiles fine and silently skips the check. Every Documents endpoint
/// whose route carries a document id must carry <see cref="DocumentAuthorizationMetadata"/>, unless it is
/// allowlisted here with the reason it is covered some other way.
/// </summary>
[Collection("Integration")]
public class DocumentAuthorizationCoverageTests(PostgresFixture fixture)
{
    private const string DocumentsPrefix = "/api/v{version:apiVersion}/documents";

    /// <summary>Endpoint name → why it carries no route-level document authorization.</summary>
    private static readonly Dictionary<string, string> Allowlist = new()
    {
        ["DeleteDocument"] = "Single-id front end for BulkDeleteDocumentsCommand, which authorizes each id inline.",
        ["RestoreDocument"] = "Single-id front end for RestoreDocumentsCommand, which filters through IDocumentQueryService.WritableDocuments.",
        ["PermanentlyDeleteDocument"] = "Single-id front end for PermanentlyDeleteDocumentsCommand, which filters through IDocumentQueryService.WritableDocuments.",
        ["GetParty"] = "{id} is a party id, not a document id.",
        ["UpdateParty"] = "{id} is a party id, not a document id.",
        ["DeleteParty"] = "{id} is a party id, not a document id.",
        ["PatchTag"] = "{id} is a tag id, not a document id.",
        ["DeleteTag"] = "{id} is a tag id, not a document id.",
    };

    [Fact]
    public void DocumentRouteEndpoints_RequireDocumentAuthorization()
    {
        var documentIdRoutes = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(DocumentsPrefix, StringComparison.Ordinal) == true)
            .Where(e => e.RoutePattern.RawText!.Contains("{documentId", StringComparison.Ordinal)
                || e.RoutePattern.RawText!.Contains("{id", StringComparison.Ordinal))
            .ToList();

        // Guards the guard: if DocumentsPrefix stopped matching the Documents group, the assertion below would
        // pass vacuously. 17 document-id endpoints authorize at the route today; the allowlisted routes are
        // counted too, so 18 stays a safe floor.
        Assert.True(documentIdRoutes.Count >= 18,
            $"Expected at least 18 Documents endpoints with a document id in the route, found {documentIdRoutes.Count}.");

        var uncovered = documentIdRoutes
            .Where(e => e.Metadata.GetMetadata<DocumentAuthorizationMetadata>() is null)
            .Where(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is not { } name
                || !Allowlist.ContainsKey(name))
            .Select(e => $"{e.DisplayName} ({e.RoutePattern.RawText})")
            .ToList();

        Assert.True(uncovered.Count == 0,
            "Documents endpoints with a document id in the route but no .RequireDocumentAuthorization(...): " +
            string.Join(", ", uncovered));
    }

    /// <summary>
    /// The operation is a separate argument on each endpoint, so copying <c>DocumentOperations.Read</c> onto a
    /// mutating endpoint compiles and would let a group Viewer write. A read verb must ask for Read and every
    /// other verb for Write.
    /// </summary>
    [Fact]
    public void DocumentAuthorizedEndpoints_RequireWrite_UnlessTheVerbReads()
    {
        var authorized = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Endpoint: e, Auth: e.Metadata.GetMetadata<DocumentAuthorizationMetadata>()))
            .Where(x => x.Auth is not null)
            .ToList();

        // Guards the guard: the 17 endpoints that carry the metadata today (the coverage test above requires it).
        Assert.True(authorized.Count >= 17,
            $"Expected at least 17 endpoints with document authorization, found {authorized.Count}.");

        var mismatched = authorized
            .Select(x =>
            {
                var methods = x.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
                var reads = methods.Count > 0 && methods.All(m => HttpMethods.IsGet(m) || HttpMethods.IsHead(m));
                var expected = reads ? DocumentOperations.Read : DocumentOperations.Write;
                return (x.Endpoint, x.Auth!.Operation, Expected: expected);
            })
            .Where(x => x.Operation.Name != x.Expected.Name)
            .Select(x => $"{x.Endpoint.DisplayName} requires {x.Operation.Name}, expected {x.Expected.Name}")
            .ToList();

        Assert.True(mismatched.Count == 0,
            "Document authorization operation does not match the HTTP verb: " + string.Join(", ", mismatched));
    }
}
