using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Parties;
using HomeSuiteHome.WebApi.Domains.Tags;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocument;

public class UpdateDocumentHandler(AppDbContext db)
    : ICommandHandler<UpdateDocumentCommand, IResult>
{
    public async Task<IResult> HandleAsync(UpdateDocumentCommand request, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags)
            .Include(d => d.DocumentParties)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct);

        if (document is null)
            return Results.NotFound();

        // Validate party IDs
        if (request.PartyIds.Count > 0)
        {
            var existingPartyIds = await db.Parties
                .Where(p => request.PartyIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct);

            var invalidIds = request.PartyIds.Except(existingPartyIds).ToList();

            if (invalidIds.Count > 0)
                return Results.BadRequest(new { InvalidPartyIds = invalidIds });
        }

        document.Name = request.Name;

        // Clear existing tag associations
        document.DocumentTags.Clear();

        // Resolve or create each tag path and create associations
        foreach (var tagPath in request.TagPaths)
        {
            var tag = await ResolveOrCreateTagAsync(tagPath, ct);
            document.DocumentTags.Add(new DocumentTag
            {
                DocumentId = document.Id,
                TagId = tag.Id
            });
        }

        // Clear existing party associations and reassign
        document.DocumentParties.Clear();

        foreach (var partyId in request.PartyIds)
        {
            document.DocumentParties.Add(new DocumentParty
            {
                DocumentId = document.Id,
                PartyId = partyId
            });
        }

        await db.SaveChangesAsync(ct);

        // Load tags for response
        var tags = await db.DocumentTags
            .AsNoTracking()
            .Where(dt => dt.DocumentId == document.Id)
            .Select(dt => new TagDto
            {
                Id = dt.Tag.Id,
                Name = dt.Tag.Name,
                Path = dt.Tag.Path
            })
            .ToListAsync(ct);

        // Load parties for response
        var parties = await db.DocumentParties
            .AsNoTracking()
            .Where(dp => dp.DocumentId == document.Id)
            .Select(dp => new PartyDto
            {
                Id = dp.Party.Id,
                Name = dp.Party.Name,
                Description = dp.Party.Description
            })
            .ToListAsync(ct);

        // Load owner info for response
        string? createdByUserName = null;
        string? ownerUserName = null;
        if (document.CreatedByUserId.HasValue)
        {
            createdByUserName = await db.Users
                .Where(u => u.Id == document.CreatedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }
        if (document.OwnerUserId.HasValue)
        {
            ownerUserName = await db.Users
                .Where(u => u.Id == document.OwnerUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }

        return Results.Ok(new UpdateDocumentResponse
        {
            Id = document.Id,
            Name = document.Name,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            FileHash = document.FileHash,
            FileExtension = document.FileExtension,
            CreatedAt = document.CreatedAt,
            CreatedByUserId = document.CreatedByUserId,
            CreatedByUserName = createdByUserName,
            OwnerUserId = document.OwnerUserId,
            OwnerUserName = ownerUserName,
            Tags = tags,
            Parties = parties
        });
    }

    private async Task<Tag> ResolveOrCreateTagAsync(string path, CancellationToken ct)
    {
        var normalizedPath = path.Trim().ToLowerInvariant().Trim('/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        Tag? parent = null;
        Tag? tag = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var currentPath = string.Join('/', segments[..(i + 1)]);

            tag = await db.Tags.FirstOrDefaultAsync(t => t.Path == currentPath, ct);

            if (tag is null)
            {
                tag = new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = segments[i],
                    Path = currentPath,
                    ParentId = parent?.Id,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Tags.Add(tag);
            }

            parent = tag;
        }

        return tag!;
    }
}
