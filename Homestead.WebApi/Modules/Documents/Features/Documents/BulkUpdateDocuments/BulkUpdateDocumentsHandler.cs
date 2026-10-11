using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkUpdateDocuments;

public class BulkUpdateDocumentsHandler(AppDbContext db,
    IAuditContext audit, IUnitOfWork unitOfWork, IDocumentMetadataService metadataService,
    IDocumentQueryService queryService, ICurrentUserAccessor currentUser)
{
    public async Task<BulkUpdateDocumentsResponse> HandleAsync(BulkUpdateDocumentsCommand request, CancellationToken ct)
    {
        var documents = await db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties)
            .Where(d => request.Ids.Contains(d.Id))
            .ToListAsync(ct);

        if (documents.Count == 0)
            return new BulkUpdateDocumentsResponse(0);

        // All-or-nothing: single-query write access check
        var documentIds = db.Documents.Where(d => request.Ids.Contains(d.Id)).Select(d => d.Id);
        await queryService.EnsureAllDocumentsWritableAsync(documentIds, ct);

        // Resolve tags and validate parties upfront
        List<Tag>? resolvedTags = null;
        if (request.TagPaths is { Count: > 0 })
        {
            resolvedTags = await metadataService.ResolveTagPathsAsync(request.TagPaths, ct);
        }

        List<Guid>? validatedPartyIds = null;
        if (request.PartyIds is { Count: > 0 })
        {
            await metadataService.ValidatePartyIdsAsync(request.PartyIds, ct);
            validatedPartyIds = request.PartyIds;
        }

        var updatedCount = 0;
        foreach (var document in documents)
        {
            var before = DocumentSnapshot.From(document);

            // Apply removals first
            if (request.RemoveTagPaths is { Count: > 0 })
            {
                metadataService.RemoveTags(document, request.RemoveTagPaths);
            }

            if (request.RemovePartyIds is { Count: > 0 })
            {
                metadataService.RemoveParties(document, request.RemovePartyIds);
            }

            // Then additions
            if (resolvedTags != null)
            {
                metadataService.AddTags(document, resolvedTags);
            }

            if (validatedPartyIds != null)
            {
                metadataService.AddParties(document, validatedPartyIds);
            }

            var after = DocumentSnapshot.From(document);
            var changes = before.Diff(after);

            if (changes.Count > 0)
            {
                document.UpdatedAt = DateTimeOffset.UtcNow;
                document.UpdatedByUserId = currentUser.UserId;
                // Diff already computed above (drives stamping + count); hand it to the audit context
                // so it flushes through the same chokepoint as every other document mutation.
                audit.Track(() => changes, "Document", document.Id);
                updatedCount++;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new BulkUpdateDocumentsResponse(updatedCount);
    }
}
