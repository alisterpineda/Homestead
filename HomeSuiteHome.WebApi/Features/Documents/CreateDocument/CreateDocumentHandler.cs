using HomeSuiteHome.WebApi.Common;

namespace HomeSuiteHome.WebApi.Features.Documents.CreateDocument;

public class CreateDocumentHandler : ICommandHandler<CreateDocumentRequest, CreateDocumentResponse>
{
    public Task<CreateDocumentResponse> HandleAsync(CreateDocumentRequest request, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}