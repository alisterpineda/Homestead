using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Documents.v1.UpdateTag;

public class UpdateTagHandler(AppDbContext db)
    : ICommandHandler<UpdateTagCommand, UpdateTagResponse?>
{
    public async Task<UpdateTagResponse?> HandleAsync(UpdateTagCommand request, CancellationToken ct)
    {
        var tag = await db.Tags
            .FirstOrDefaultAsync(t => t.Id == request.Id, ct);

        if (tag is null)
            return null;

        tag.Color = request.Color;

        await db.SaveChangesAsync(ct);

        return new UpdateTagResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Path = tag.Path,
            ParentId = tag.ParentId,
            Color = tag.Color
        };
    }
}
