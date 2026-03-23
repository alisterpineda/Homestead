using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;

public record DeleteDocumentVersionRequest(Guid DocumentId, Guid Id) : IRequest<bool>, IRequireDocumentAuthorization
{
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
