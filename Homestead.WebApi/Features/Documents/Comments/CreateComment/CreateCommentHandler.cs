using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using MediatR;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.Comments.CreateComment;

public class CreateCommentHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit)
    : IRequestHandler<CreateCommentCommand, CreateCommentResponse>
{
    public async Task<CreateCommentResponse> Handle(CreateCommentCommand request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            throw new EntityNotFoundException("Document not found.");

        var userId = currentUser.UserId;

        var comment = new DocumentComment
        {
            Id = Guid.NewGuid(),
            Body = request.Body.Trim(),
            DocumentId = request.DocumentId,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.DocumentComments.Add(comment);

        audit.Created("DocumentComment", comment.Id, parentEntityId: request.DocumentId);
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
