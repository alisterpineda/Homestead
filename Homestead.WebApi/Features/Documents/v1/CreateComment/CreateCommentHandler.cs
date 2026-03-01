using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public class CreateCommentHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger)
    : ICommandHandler<CreateCommentRequest, IResult>
{
    public async Task<IResult> HandleAsync(CreateCommentRequest request, CancellationToken ct)
    {
        var documentExists = await db.Documents.AnyAsync(d => d.Id == request.DocumentId, ct);
        if (!documentExists)
            return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest("Comment body cannot be empty.");

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

        var response = new CreateCommentResponse
        {
            Id = comment.Id,
            Body = comment.Body,
            CreatedAt = comment.CreatedAt,
            CreatedByUserId = comment.CreatedByUserId
        };

        return Results.Created($"/api/v1/documents/{request.DocumentId}/comments/{comment.Id}", response);
    }
}
