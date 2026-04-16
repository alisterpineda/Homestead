using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.DeleteDocumentVersion;

public record DeleteDocumentVersionCommand(Guid DocumentId, Guid Id) : IRequest<bool>, IRequireDocumentAuthorization
{
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
