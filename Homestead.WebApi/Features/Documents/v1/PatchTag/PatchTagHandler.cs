using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.PatchTag;

public class PatchTagHandler(AppDbContext db, IDocumentAccessService accessService)
    : ICommandHandler<PatchTagCommand, IResult>
{
    public async Task<IResult> HandleAsync(PatchTagCommand request, CancellationToken ct)
    {
        var tag = await db.Tags
            .FirstOrDefaultAsync(t => t.Id == request.Id, ct);

        if (tag is null)
            return Results.NotFound();

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

        // Populate model from current DB state
        var model = new PatchTagModel
        {
            Name = tag.Name,
            ParentId = tag.ParentId,
            Color = tag.Color
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

        // Validate name
        if (string.IsNullOrWhiteSpace(model.Name))
            return Results.BadRequest(new { Error = "Tag name cannot be empty." });

        if (model.Name.Contains('/'))
            return Results.BadRequest(new { Error = "Tag name cannot contain '/' characters." });

        var normalizedName = model.Name.Trim().ToLowerInvariant();

        // Validate parent
        if (model.ParentId == request.Id)
            return Results.BadRequest(new { Error = "A tag cannot be its own parent." });

        string? parentPath = null;
        if (model.ParentId is not null)
        {
            var parent = await db.Tags
                .FirstOrDefaultAsync(t => t.Id == model.ParentId, ct);

            if (parent is null)
                return Results.BadRequest(new { Error = "Parent tag not found." });

            // Check for circular reference: new parent must not be a descendant of this tag
            if (parent.Path.StartsWith(oldPath + "/") || parent.Path == oldPath)
                return Results.BadRequest(new { Error = "Cannot move a tag under one of its own descendants." });

            parentPath = parent.Path;
        }

        // Update entity from patched model
        tag.Name = normalizedName;
        tag.ParentId = model.ParentId;
        tag.Color = model.Color;

        // Recompute path if name or parent changed
        var nameChanged = normalizedName != oldName;
        var parentChanged = model.ParentId != oldParentId;

        if (nameChanged || parentChanged)
        {
            var newPath = parentPath is not null
                ? parentPath + "/" + normalizedName
                : normalizedName;

            // Check for path uniqueness
            var conflictExists = await db.Tags
                .AnyAsync(t => t.Path == newPath && t.Id != request.Id, ct);

            if (conflictExists)
                return Results.Conflict(new { Error = $"A tag with path '{newPath}' already exists." });

            tag.Path = newPath;

            // Bulk-update all descendant paths
            var descendants = await db.Tags
                .Where(t => t.Path.StartsWith(oldPath + "/"))
                .ToListAsync(ct);

            foreach (var descendant in descendants)
            {
                descendant.Path = newPath + descendant.Path[oldPath.Length..];
            }
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new PatchTagResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Path = tag.Path,
            ParentId = tag.ParentId,
            Color = tag.Color
        });
    }
}
