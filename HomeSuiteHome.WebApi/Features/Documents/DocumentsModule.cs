using HomeSuiteHome.WebApi.Features.Documents.CreateDocument;
using HomeSuiteHome.WebApi.Features.Documents.GetDocuments;

namespace HomeSuiteHome.WebApi.Features.Documents;

public static class DocumentsModule
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents")
            .WithTags("Documents");

        CreateDocumentEndpoint.Map(group);
        GetDocumentsEndpoint.Map(group);

        return app;
    }
}