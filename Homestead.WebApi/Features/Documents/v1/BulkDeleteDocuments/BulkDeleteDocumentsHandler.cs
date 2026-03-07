using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;

public class BulkDeleteDocumentsHandler(AppDbContext db, ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger, IAuthorizationService authService, IHttpContextAccessor httpContext)
    : ICommandHandler<BulkDeleteDocumentsRequest, BulkDeleteDocumentsResponse>
{
    public async Task<BulkDeleteDocumentsResponse> HandleAsync(BulkDeleteDocumentsRequest request, CancellationToken ct)
    {
        var documents = await db.Documents
            .Where(d => request.Ids.Contains(d.Id))
            .ToListAsync(ct);

        if (documents.Count == 0)
            return new BulkDeleteDocumentsResponse(0);

        // All-or-nothing: throws ForbiddenAccessException on first unauthorized document
        foreach (var doc in documents)
        {
            await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, doc.Id, DocumentOperations.Write);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var doc in documents)
        {
            doc.DeletedAt = now;
            doc.DeletedByUserId = currentUser.UserId;
            auditLogger.LogSoftDeleted("Document", doc.Id, currentUser.UserId, description: doc.Name);
        }

        await db.SaveChangesAsync(ct);

        return new BulkDeleteDocumentsResponse(documents.Count);
    }
}
