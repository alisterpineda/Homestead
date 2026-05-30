using Homestead.WebApi.Authorization;
using MediatR;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;

public record CreateCommentCommand(string Body) : ICommand<CreateCommentResponse>, IRequireDocumentAuthorization
{
    internal Guid DocumentId { get; init; }

    Guid IRequireDocumentAuthorization.DocumentId => DocumentId;
    public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Write;
}
