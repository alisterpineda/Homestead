using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.CreateTag;

public class CreateTagHandler(AppDbContext db)
    : ICommandHandler<CreateTagRequest, CreateTagResponse>
{
    public async Task<CreateTagResponse> HandleAsync(CreateTagRequest request, CancellationToken ct)
    {
        var normalizedPath = request.Path.Trim().ToLowerInvariant().Trim('/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            throw new ArgumentException("Tag path cannot be empty.");

        Tag? parent = null;
        Tag? tag = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var currentPath = string.Join('/', segments[..( i + 1)]);

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

        await db.SaveChangesAsync(ct);

        return new CreateTagResponse
        {
            Id = tag!.Id,
            Name = tag.Name,
            Path = tag.Path,
            ParentId = tag.ParentId
        };
    }
}
