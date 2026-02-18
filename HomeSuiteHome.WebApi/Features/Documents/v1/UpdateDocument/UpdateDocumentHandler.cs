using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Domains.Tags;
using HomeSuiteHome.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocument;

public class UpdateDocumentHandler(AppDbContext db)
    : ICommandHandler<UpdateDocumentRequest, UpdateDocumentResponse?>
{
    public async Task<UpdateDocumentResponse?> HandleAsync(UpdateDocumentRequest request, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.DocumentTags)
            .FirstOrDefaultAsync(d => d.Id == request.Id, ct);

        if (document is null)
            return null;

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

        return new UpdateDocumentResponse
        {
            Id = document.Id,
            Name = document.Name,
            FileName = document.FileName,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            FileHash = document.FileHash,
            CreatedAt = document.CreatedAt,
            Tags = tags
        };
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
