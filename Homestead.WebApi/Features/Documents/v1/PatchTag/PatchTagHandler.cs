using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.PatchTag;

public class PatchTagHandler(AppDbContext db, IDocumentQueryService accessService, IAuditLogger auditLogger)
    : ICommandHandler<PatchTagCommand, PatchTagResponse>
{
    public async Task<PatchTagResponse> HandleAsync(PatchTagCommand request, CancellationToken ct)
    {
        var tag = await db.Tags
            .FirstOrDefaultAsync(t => t.Id == request.Id, ct);

        if (tag is null)
            throw new NotFoundException("Tag not found.");

        var before = TagSnapshot.From(tag);

        // Block modification if the tag (or its descendants) is used on inaccessible documents
        var subtreeTagIds = db.Tags
            .Where(t => t.Id == request.Id || t.Path.StartsWith(tag.Path + "/"))
            .Select(t => t.Id);

        var relatedDocIds = db.DocumentTags
            .Where(dt => subtreeTagIds.Contains(dt.TagId))
            .Select(dt => dt.DocumentId);

        await accessService.EnsureAllDocumentsAccessibleAsync(relatedDocIds, ct);

        var oldPath = tag.Path;
        var oldName = tag.Name;
        var oldParentId = tag.ParentId;
        var oldEffectiveColor = tag.EffectiveColor;

        // Populate model from current DB state
        var model = new PatchTagModel
        {
            Name = tag.Name,
            ParentId = tag.ParentId,
            Color = tag.Color,
            Description = tag.Description
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

        // Validate name
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BadRequestException("Tag name cannot be empty.");

        if (model.Name.Contains('/'))
            throw new BadRequestException("Tag name cannot contain '/' characters.");

        var normalizedName = model.Name.Trim();

        // Validate parent
        if (model.ParentId == request.Id)
            throw new BadRequestException("A tag cannot be its own parent.");

        string? parentPath = null;
        string? newParentEffectiveColor = null;
        if (model.ParentId is not null)
        {
            var parent = await db.Tags
                .FirstOrDefaultAsync(t => t.Id == model.ParentId, ct);

            if (parent is null)
                throw new BadRequestException("Parent tag not found.");

            // Check for circular reference: new parent must not be a descendant of this tag
            if (parent.Path.StartsWith(oldPath + "/") || parent.Path == oldPath)
                throw new BadRequestException("Cannot move a tag under one of its own descendants.");

            parentPath = parent.Path;
            newParentEffectiveColor = parent.EffectiveColor;
        }

        // Update entity from patched model
        tag.Name = normalizedName;
        tag.ParentId = model.ParentId;
        tag.Color = model.Color;
        tag.Description = model.Description;

        // Recompute path if name or parent changed
        var nameChanged = normalizedName != oldName;
        var parentChanged = model.ParentId != oldParentId;

        tag.EffectiveColor = tag.Color ?? newParentEffectiveColor;
        var effectiveColorChanged = tag.EffectiveColor != oldEffectiveColor;

        List<Tag>? descendants = null;

        if (nameChanged || parentChanged)
        {
            var newPath = parentPath is not null
                ? parentPath + "/" + normalizedName
                : normalizedName;

            // Check for path uniqueness
            var conflictExists = await db.Tags
                .AnyAsync(t => t.Path == newPath && t.Id != request.Id, ct);

            if (conflictExists)
                throw new ConflictException($"A tag with path '{newPath}' already exists.");

            tag.Path = newPath;

            // Bulk-update all descendant paths (and reuse for color cascade)
            descendants = await db.Tags
                .Where(t => t.Path.StartsWith(oldPath + "/"))
                .ToListAsync(ct);

            foreach (var descendant in descendants)
            {
                descendant.Path = newPath + descendant.Path[oldPath.Length..];
            }
        }

        if (effectiveColorChanged)
        {
            descendants ??= await db.Tags
                .Where(t => t.Path.StartsWith(tag.Path + "/"))
                .ToListAsync(ct);

            EffectiveColorResolver.Recompute(tag, newParentEffectiveColor, descendants);
        }

        var after = TagSnapshot.From(tag);
        var changes = before.Diff(after);
        auditLogger.LogUpdated("Tag", tag.Id, changes);

        await db.SaveChangesAsync(ct);

        return new PatchTagResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Path = tag.Path,
            ParentId = tag.ParentId,
            Color = tag.Color,
            EffectiveColor = tag.EffectiveColor,
            Description = tag.Description
        };
    }
}
