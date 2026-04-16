using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersions;

public record GetDocumentVersionsQuery : PaginatedRequest, IRequest<PaginatedResponse<GetDocumentVersionsResponse>>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
}
