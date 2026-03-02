using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Features.Documents.v1.CreateTag;

public class CreateTagHandler(AppDbContext db, ITagPathResolver tagPathResolver)
    : ICommandHandler<CreateTagRequest, CreateTagResponse>
{
    public async Task<CreateTagResponse> HandleAsync(CreateTagRequest request, CancellationToken ct)
    {
        var tag = await tagPathResolver.ResolveOrCreateAsync(request.Path, request.Color, ct);

        await db.SaveChangesAsync(ct);

        return new CreateTagResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Path = tag.Path,
            ParentId = tag.ParentId,
            Color = tag.Color
        };
    }
}
