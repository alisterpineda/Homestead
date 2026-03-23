using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.v1.GetDocument;

public record GetDocumentRequest(Guid Id) : IRequest<GetDocumentResponse?>, IRequireDocumentAuthorization
{
    Guid IRequireDocumentAuthorization.DocumentId => Id;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
}
