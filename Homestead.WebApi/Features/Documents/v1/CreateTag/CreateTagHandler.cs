using Homestead.WebApi.Authentication;
using MediatR;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Features.Documents.v1.CreateTag;

public class CreateTagHandler(AppDbContext db, ITagPathResolver tagPathResolver, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : IRequestHandler<CreateTagRequest, CreateTagResponse>
{
    public async Task<CreateTagResponse> Handle(CreateTagRequest request, CancellationToken ct)
    {
        var tag = await tagPathResolver.ResolveOrCreateAsync(request.Path, leafColor: request.Color, leafDescription: request.Description, createdByUserId: currentUser.UserId, ct: ct);

        auditLogger.LogCreated("Tag", tag.Id);

        await db.SaveChangesAsync(ct);

        return new CreateTagResponse
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
