using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersion;

public record GetDocumentVersionQuery(Guid DocumentId, Guid Id) : IRequest<GetDocumentVersionResponse?>, IRequireDocumentAuthorization
{
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
}
