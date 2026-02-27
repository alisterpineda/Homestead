using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Domains.Tags;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocument;

public class PatchDocumentHandler(AppDbContext db)
    : ICommandHandler<PatchDocumentCommand, IResult>
{
    public async Task<IResult> HandleAsync(PatchDocumentCommand request, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct);

        if (document is null)
            return Results.NotFound();

        // Populate model from current DB state
        var model = new PatchDocumentModel
        {
            Name = document.Name,
            DocumentDate = document.DocumentDate,
            ValidFrom = document.ValidFrom,
            ValidUntil = document.ValidUntil,
            TagPaths = document.DocumentTags.Select(dt => dt.Tag.Path).ToList(),
            PartyIds = document.DocumentParties.Select(dp => dp.PartyId).ToList()
        };

        // Apply patch operations
        var errors = new List<JsonPatchError>();
        request.PatchDocument.ApplyTo(model, error => errors.Add(error));

        if (errors.Count > 0)
            return Results.BadRequest(new
            {
                Errors = errors.Select(e => new
                {
                    Operation = e.Operation?.ToString(),
                    e.ErrorMessage
                })
            });

        // Validate
        if (string.IsNullOrWhiteSpace(model.Name))
            return Results.BadRequest(new { Error = "Name is required." });

        // Validate party IDs
        if (model.PartyIds.Count > 0)
        {
            var existingPartyIds = await db.Parties
                .Where(p => model.PartyIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct);

            var invalidIds = model.PartyIds.Except(existingPartyIds).ToList();

            if (invalidIds.Count > 0)
                return Results.BadRequest(new { InvalidPartyIds = invalidIds });
        }

        // Update entity from patched model
        document.Name = model.Name;
        document.DocumentDate = model.DocumentDate;
        document.ValidFrom = model.ValidFrom;
        document.ValidUntil = model.ValidUntil;

        // Clear existing tag associations
        document.DocumentTags.Clear();

        // Resolve or create each tag path and create associations
        foreach (var tagPath in model.TagPaths)
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

        foreach (var partyId in model.PartyIds)
        {
            document.DocumentParties.Add(new DocumentParty
            {
                DocumentId = document.Id,
                PartyId = partyId
            });
        }

        await db.SaveChangesAsync(ct);

        await SearchVectorHelper.UpdateSearchVectorAsync(db, document.Id, ct);

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

        // Load latest version for file fields
        var latestVersion = await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == document.Id)
            .OrderByDescending(r => r.VersionNumber)
            .FirstAsync(ct);

        return Results.Ok(new PatchDocumentResponse
        {
            Id = document.Id,
            Name = document.Name,
            ContentType = latestVersion.ContentType,
            FileSize = latestVersion.FileSize,
            FileHash = latestVersion.FileHash,
            FileExtension = latestVersion.FileExtension,
            CreatedAt = document.CreatedAt,
            DocumentDate = document.DocumentDate,
            ValidFrom = document.ValidFrom,
            ValidUntil = document.ValidUntil,
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
