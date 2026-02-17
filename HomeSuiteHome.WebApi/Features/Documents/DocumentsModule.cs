using Asp.Versioning.Builder;
using HomeSuiteHome.WebApi.Features.Documents.v1.CreateDocument;
using HomeSuiteHome.WebApi.Features.Documents.v1.GetDocuments;

namespace HomeSuiteHome.WebApi.Features.Documents;

public static class DocumentsModule
{
    public static IVersionedEndpointRouteBuilder MapDocumentsEndpoints(
        this IVersionedEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}/documents")
            .HasApiVersion(1, 0)
            .WithGroupName("v1")
            .WithTags("Documents");

        CreateDocumentEndpoint.Map(v1);
        GetDocumentsEndpoint.Map(v1);

        return app;
    }
}