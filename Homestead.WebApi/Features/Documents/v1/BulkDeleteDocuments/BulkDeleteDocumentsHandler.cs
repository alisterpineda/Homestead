using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;

public class BulkDeleteDocumentsHandler(AppDbContext db, IConfiguration configuration, ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger, IAuthorizationService authService, IHttpContextAccessor httpContext)
    : ICommandHandler<BulkDeleteDocumentsRequest, BulkDeleteDocumentsResponse>
{
    public async Task<BulkDeleteDocumentsResponse> HandleAsync(BulkDeleteDocumentsRequest request, CancellationToken ct)
    {
        var documents = await db.Documents
            .Include(d => d.Versions)
            .Where(d => request.Ids.Contains(d.Id))
            .ToListAsync(ct);

        if (documents.Count == 0)
            return new BulkDeleteDocumentsResponse(0);

        // Filter to only documents the user has write access to
        var authorizedDocs = new List<Domains.Documents.Document>();
        foreach (var doc in documents)
        {
            var result = await authService.AuthorizeAsync(httpContext.HttpContext!.User, doc.Id, DocumentOperations.Write);
            if (result.Succeeded)
                authorizedDocs.Add(doc);
        }

        if (authorizedDocs.Count == 0)
            return new BulkDeleteDocumentsResponse(0);

        var mediaPath = configuration["FileStorage:MediaPath"]
            ?? throw new InvalidOperationException("FileStorage:MediaPath is not configured.");

        var physicalPaths = authorizedDocs
            .SelectMany(d => d.Versions)
            .Select(r => r.GetPhysicalPath(mediaPath))
            .ToList();

        foreach (var doc in authorizedDocs)
        {
            auditLogger.LogDeleted("Document", doc.Id, currentUser.UserId, description: doc.Name);
        }

        db.Documents.RemoveRange(authorizedDocs);
        await db.SaveChangesAsync(ct);

        foreach (var path in physicalPaths)
        {
            File.Delete(path);
        }

        return new BulkDeleteDocumentsResponse(authorizedDocs.Count);
    }
}
