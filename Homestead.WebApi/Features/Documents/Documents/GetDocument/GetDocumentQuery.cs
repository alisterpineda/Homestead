using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.Documents.GetDocument;

public record GetDocumentQuery(Guid Id) : IRequest<GetDocumentResponse?>, IRequireDocumentAuthorization
{
    Guid IRequireDocumentAuthorization.DocumentId => Id;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
}
