using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.Documents.DeleteDocumentVersion;

public record DeleteDocumentVersionCommand(Guid DocumentId, Guid Id) : IRequest<bool>, IRequireDocumentAuthorization
{
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
