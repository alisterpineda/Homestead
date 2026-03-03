using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public class CreateCommentHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger,
    IDocumentAccessService accessService)
    : ICommandHandler<CreateCommentRequest, CreateCommentResponse>
{
    public async Task<CreateCommentResponse> HandleAsync(CreateCommentRequest request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            throw new NotFoundException("Document not found.");

        var access = await accessService.CheckAccessAsync(request.DocumentId, DocumentOperation.Read, ct);
        if (!access.IsAllowed)
            throw new ForbiddenAccessException();

        if (string.IsNullOrWhiteSpace(request.Body))
            throw new BadRequestException("Comment body cannot be empty.");

        var userId = currentUser.UserId;

        var comment = new DocumentComment
        {
            Id = Guid.NewGuid(),
            Body = request.Body.Trim(),
            DocumentId = request.DocumentId,
            CreatedByUserId = userId
        };

        db.DocumentComments.Add(comment);

        auditLogger.LogCreated("DocumentComment", comment.Id, userId, parentEntityId: request.DocumentId);

        await db.SaveChangesAsync(ct);

        return new CreateCommentResponse
        {
            Id = comment.Id,
            Body = comment.Body,
            CreatedAt = comment.CreatedAt,
            CreatedByUserId = comment.CreatedByUserId
        };
    }
}
