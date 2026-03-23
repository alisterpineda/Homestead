using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public class CreateCommentHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<CreateCommentCommand, CreateCommentResponse>
{
    public async Task<CreateCommentResponse> Handle(CreateCommentCommand request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            throw new NotFoundException("Document not found.");

        var userId = currentUser.UserId;

        var comment = new DocumentComment
        {
            Id = Guid.NewGuid(),
            Body = request.Body.Trim(),
            DocumentId = request.DocumentId,
            CreatedByUserId = userId
        };

        db.DocumentComments.Add(comment);

        auditLogger.LogCreated("DocumentComment", comment.Id, parentEntityId: request.DocumentId);

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
