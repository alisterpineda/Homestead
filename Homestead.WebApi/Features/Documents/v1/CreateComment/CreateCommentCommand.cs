using Homestead.WebApi.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public record CreateCommentCommand(string Body) : IRequest<CreateCommentResponse>, IRequireDocumentAuthorization
{
    internal Guid DocumentId { get; init; }

    Guid IRequireDocumentAuthorization.DocumentId => DocumentId;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
