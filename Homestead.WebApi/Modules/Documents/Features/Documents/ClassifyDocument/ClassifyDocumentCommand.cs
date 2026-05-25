using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public record ClassifyDocumentCommand : IRequest<ClassifyDocumentResponse>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public Guid? AiProviderId { get; init; }
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
