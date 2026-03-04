using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocument;

public class PatchDocumentHandler(AppDbContext db, ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger, ISearchVectorUpdater searchVectorUpdater, ITagPathResolver tagPathResolver,
    IAuthorizationService authService, IHttpContextAccessor httpContext, IDocumentQueryService queryService)
    : ICommandHandler<PatchDocumentCommand, PatchDocumentResponse>
{
    public async Task<PatchDocumentResponse> HandleAsync(PatchDocumentCommand request, CancellationToken ct)
    {
        await authService.AuthorizeDocumentAsync(httpContext.HttpContext!.User, request.Id, DocumentOperations.Write);

        var document = await db.Documents
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct);

        if (document is null)
            throw new NotFoundException("Document not found.");

        var before = DocumentSnapshot.From(document);

        // Populate model from current DB state
        var model = new PatchDocumentModel
        {
            Name = document.Name,
            DocumentDate = document.DocumentDate,
            ValidFrom = document.ValidFrom,
            ValidUntil = document.ValidUntil,
            TagPaths = document.DocumentTags.Select(dt => dt.Tag.Path).ToList(),
            PartyIds = document.DocumentParties.Select(dp => dp.PartyId).ToList(),
            GroupId = document.GroupId
        };

        // Apply patch operations
        var errors = new List<JsonPatchError>();
        request.PatchDocument.ApplyTo(model, error => errors.Add(error));

        if (errors.Count > 0)
            throw new BadRequestException(errors.Select(e => new
            {
                Operation = e.Operation?.ToString(),
                e.ErrorMessage
            }));

        // Validate
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BadRequestException("Name is required.");

        // Validate party IDs
        if (model.PartyIds.Count > 0)
        {
            var existingPartyIds = await db.Parties
                .Where(p => model.PartyIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct);

            var invalidIds = model.PartyIds.Except(existingPartyIds).ToList();

            if (invalidIds.Count > 0)
                throw new BadRequestException("One or more party IDs are invalid.");
        }

        // Validate GroupId if changed
        if (model.GroupId.HasValue && model.GroupId != document.GroupId)
        {
            var groupExists = await db.Groups.AnyAsync(g => g.Id == model.GroupId.Value, ct);
            if (!groupExists)
                throw new BadRequestException("Group not found.");

            var canWriteToGroup = await queryService.CanWriteToGroupAsync(model.GroupId.Value, ct);
            if (!canWriteToGroup)
                throw new ForbiddenAccessException();
        }

        // Update entity from patched model
        document.Name = model.Name;
        document.DocumentDate = model.DocumentDate;
        document.ValidFrom = model.ValidFrom;
        document.ValidUntil = model.ValidUntil;
        document.GroupId = model.GroupId;

        // Clear existing tag associations
        document.DocumentTags.Clear();

        // Resolve or create each tag path and create associations
        foreach (var tagPath in model.TagPaths)
        {
            var tag = await tagPathResolver.ResolveOrCreateAsync(tagPath, createdByUserId: currentUser.UserId, ct: ct);
            document.DocumentTags.Add(new DocumentTag
            {
                DocumentId = document.Id,
                TagId = tag.Id,
                Tag = tag
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

        var after = DocumentSnapshot.From(document);
        var changes = before.Diff(after);
        auditLogger.LogUpdated("Document", document.Id, currentUser.UserId, changes);

        await db.SaveChangesAsync(ct);

        await searchVectorUpdater.UpdateSearchVectorAsync(document.Id, ct);

        // Load tags for response
        var tags = await db.DocumentTags
            .AsNoTracking()
            .Where(dt => dt.DocumentId == document.Id)
            .Select(dt => new TagDto
            {
                Id = dt.Tag.Id,
                Name = dt.Tag.Name,
                Path = dt.Tag.Path,
                Color = dt.Tag.Color
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

        // Load group info
        string? groupName = null;
        if (document.GroupId.HasValue)
        {
            groupName = await db.Groups
                .Where(g => g.Id == document.GroupId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync(ct);
        }

        // Load latest version for file fields
        var latestVersion = await db.DocumentVersions
            .AsNoTracking()
            .Where(r => r.DocumentId == document.Id)
            .OrderByDescending(r => r.VersionNumber)
            .FirstAsync(ct);

        return new PatchDocumentResponse
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
            GroupId = document.GroupId,
            GroupName = groupName,
            Tags = tags,
            Parties = parties
        };
    }

}
