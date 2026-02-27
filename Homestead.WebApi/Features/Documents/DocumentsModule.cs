using Asp.Versioning.Builder;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;
using Homestead.WebApi.Features.Documents.v1.CreateDocument;
using Homestead.WebApi.Features.Documents.v1.CreateDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.CreateParty;
using Homestead.WebApi.Features.Documents.v1.DeleteDocument;
using Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.DeleteParty;
using Homestead.WebApi.Features.Documents.v1.GetDocument;
using Homestead.WebApi.Features.Documents.v1.GetDocumentFile;
using Homestead.WebApi.Features.Documents.v1.GetDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;
using Homestead.WebApi.Features.Documents.v1.GetDocuments;
using Homestead.WebApi.Features.Documents.v1.GetParties;
using Homestead.WebApi.Features.Documents.v1.GetParty;
using Homestead.WebApi.Features.Documents.v1.UpdateDocument;
using Homestead.WebApi.Features.Documents.v1.UpdateDocumentVersion;
using Homestead.WebApi.Features.Documents.v1.UpdateParty;

namespace Homestead.WebApi.Features.Documents;

public static class DocumentsModule
{
    public static IVersionedEndpointRouteBuilder MapDocumentsEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/documents")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Documents")
            .RequireAuthorization();

        BulkDeleteDocumentsEndpoint.Map(v1);
        CreateDocumentEndpoint.Map(v1);
        DeleteDocumentEndpoint.Map(v1);
        GetDocumentEndpoint.Map(v1);
        GetDocumentFileEndpoint.Map(v1);
        GetDocumentsEndpoint.Map(v1);
        UpdateDocumentEndpoint.Map(v1);

        var versions = v1.MapGroup("/{documentId:guid}/versions");
        CreateDocumentVersionEndpoint.Map(versions);
        DeleteDocumentVersionEndpoint.Map(versions);
        GetDocumentVersionsEndpoint.Map(versions);
        GetDocumentVersionEndpoint.Map(versions);
        UpdateDocumentVersionEndpoint.Map(versions);

        var parties = v1.MapGroup("/parties");
        CreatePartyEndpoint.Map(parties);
        DeletePartyEndpoint.Map(parties);
        GetPartiesEndpoint.Map(parties);
        GetPartyEndpoint.Map(parties);
        UpdatePartyEndpoint.Map(parties);

        return app;
    }
}
