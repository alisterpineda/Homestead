using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Features.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;

public class BulkDeleteDocumentsHandler(AppDbContext db, ICurrentUserAccessor currentUser,
    IAuditContext audit, IAuthorizationService authService, IHttpContextAccessor httpContext)
    : IRequestHandler<BulkDeleteDocumentsCommand, BulkDeleteDocumentsResponse>
{
    public async Task<BulkDeleteDocumentsResponse> Handle(BulkDeleteDocumentsCommand request, CancellationToken ct)
    {
        var documents = await db.Documents
            .Where(d => request.Ids.Contains(d.Id))
            .ToListAsync(ct);

        if (documents.Count == 0)
            return new BulkDeleteDocumentsResponse(0);

        // All-or-nothing: throws AccessDeniedException on first unauthorized document
        foreach (var doc in documents)
        {
            await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, doc.Id, DocumentOperations.Write);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var doc in documents)
        {
            doc.DeletedAt = now;
            doc.DeletedByUserId = currentUser.UserId;
            audit.SoftDeleted("Document", doc.Id, description: doc.Name);
        }

        await db.SaveChangesAsync(ct);

        return new BulkDeleteDocumentsResponse(documents.Count);
    }
}
