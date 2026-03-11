using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1;

public class DocumentMetadataService(AppDbContext db, ITagPathResolver tagPathResolver, ICurrentUserAccessor currentUser)
    : IDocumentMetadataService
{
    public async Task<List<Tag>> ResolveTagPathsAsync(List<string> tagPaths, CancellationToken ct)
    {
        var tags = new List<Tag>();
        foreach (var tagPath in tagPaths)
        {
            var tag = await tagPathResolver.ResolveOrCreateAsync(tagPath, createdByUserId: currentUser.UserId, ct: ct);
            tags.Add(tag);
        }
        return tags;
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
            throw new BadRequestException("One or more party IDs are invalid.");
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
}
