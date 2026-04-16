using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Services;

public class DocumentMetadataService(AppDbContext db, ITagPathResolver tagPathResolver, ICurrentUserAccessor currentUser)
    : IDocumentMetadataService
{
    public async Task<List<Tag>> ResolveTagPathsAsync(List<string> tagPaths, CancellationToken ct)
    {
        return await tagPathResolver.ResolveOrCreateManyAsync(tagPaths, createdByUserId: currentUser.UserId, ct: ct);
    }

    public async Task ValidatePartyIdsAsync(List<Guid> partyIds, CancellationToken ct)
    {
        if (partyIds.Count == 0) return;

        var existingPartyIds = await db.Parties
            .Where(p => partyIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var invalidIds = partyIds.Except(existingPartyIds).ToList();
        if (invalidIds.Count > 0)
            throw new BusinessRuleException("One or more party IDs are invalid.");
    }

    public void SetTags(Document document, List<Tag> tags)
    {
        document.DocumentTags.Clear();
        AddTags(document, tags);
    }

    public void AddTags(Document document, List<Tag> tags)
    {
        var existingTagIds = new HashSet<Guid>(document.DocumentTags.Select(dt => dt.TagId));
        foreach (var tag in tags)
        {
            if (!existingTagIds.Contains(tag.Id))
            {
                document.DocumentTags.Add(new DocumentTag
                {
                    DocumentId = document.Id,
                    TagId = tag.Id,
                    Tag = tag
                });
            }
        }
    }

    public void SetParties(Document document, List<Guid> partyIds)
    {
        document.DocumentParties.Clear();
        AddParties(document, partyIds);
    }

    public void AddParties(Document document, List<Guid> partyIds)
    {
        var existingPartyIds = new HashSet<Guid>(document.DocumentParties.Select(dp => dp.PartyId));
        foreach (var partyId in partyIds)
        {
            if (!existingPartyIds.Contains(partyId))
            {
                document.DocumentParties.Add(new DocumentParty
                {
                    DocumentId = document.Id,
                    PartyId = partyId
                });
            }
        }
    }

    public void RemoveTags(Document document, List<string> tagPaths)
    {
        var pathsToRemove = new HashSet<string>(tagPaths, StringComparer.OrdinalIgnoreCase);
        var toRemove = document.DocumentTags.Where(dt => pathsToRemove.Contains(dt.Tag.Path)).ToList();
        foreach (var dt in toRemove)
            document.DocumentTags.Remove(dt);
    }

    public void RemoveParties(Document document, List<Guid> partyIds)
    {
        var idsToRemove = new HashSet<Guid>(partyIds);
        var toRemove = document.DocumentParties.Where(dp => idsToRemove.Contains(dp.PartyId)).ToList();
        foreach (var dp in toRemove)
            document.DocumentParties.Remove(dp);
    }
}
