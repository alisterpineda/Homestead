using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.Documents.GetDocumentFile;

public record GetDocumentFileQuery(Guid Id, Guid? VersionId, bool? Download = null) : IRequest<IResult>, IRequireDocumentAuthorization
{
    Guid IRequireDocumentAuthorization.DocumentId => Id;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
}
