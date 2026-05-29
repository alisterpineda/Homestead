using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;

public record SummarizeDocumentCommand : IRequest<SummarizeDocumentResponse>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public Guid? AiProviderId { get; init; }

    Guid IRequireDocumentAuthorization.DocumentId => DocumentId;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
