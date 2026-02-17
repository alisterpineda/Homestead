using HomeSuiteHome.WebApi.Features.Documents.CreateDocument;

namespace HomeSuiteHome.WebApi.Features.Documents;

public static class DocumentsModule
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents")
            .WithTags("Orders");

        CreateDocumentEndpoint.Map(group);
        
        return app;
    }
}