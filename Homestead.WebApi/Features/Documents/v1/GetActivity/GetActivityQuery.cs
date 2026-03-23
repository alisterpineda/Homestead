using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.v1.GetActivity;

public record GetActivityQuery : PaginatedRequest, IRequest<PaginatedResponse<GetActivityResponse>>, IRequireDocumentAuthorization
{
    public Guid DocumentId { get; init; }
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
}
