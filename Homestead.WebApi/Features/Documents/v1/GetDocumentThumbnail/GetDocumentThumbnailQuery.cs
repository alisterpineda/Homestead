using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentThumbnail;

public record GetDocumentThumbnailQuery(Guid Id, Guid? VersionId) : IRequest<IResult>, IRequireDocumentAuthorization
{
    Guid IRequireDocumentAuthorization.DocumentId => Id;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
}
