using Asp.Versioning.Builder;
using HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocument;
using HomeSuiteHome.WebApi.Features.Documents.v1.CreateParty;
using HomeSuiteHome.WebApi.Features.Documents.v1.DeleteDocument;
using HomeSuiteHome.WebApi.Features.Documents.v1.DeleteParty;
using HomeSuiteHome.WebApi.Features.Documents.v1.GetDocument;
using HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentFile;
using HomeSuiteHome.WebApi.Features.Documents.v1.GetDocuments;
using HomeSuiteHome.WebApi.Features.Documents.v1.GetParties;
using HomeSuiteHome.WebApi.Features.Documents.v1.GetParty;
using HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocument;
using HomeSuiteHome.WebApi.Features.Documents.v1.UpdateParty;

namespace HomeSuiteHome.WebApi.Features.Documents;

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

        CreateDocumentEndpoint.Map(v1);
        DeleteDocumentEndpoint.Map(v1);
        GetDocumentEndpoint.Map(v1);
        GetDocumentFileEndpoint.Map(v1);
        GetDocumentsEndpoint.Map(v1);
        UpdateDocumentEndpoint.Map(v1);

        var parties = v1.MapGroup("/parties");
        CreatePartyEndpoint.Map(parties);
        DeletePartyEndpoint.Map(parties);
        GetPartiesEndpoint.Map(parties);
        GetPartyEndpoint.Map(parties);
        UpdatePartyEndpoint.Map(parties);

        return app;
    }
}