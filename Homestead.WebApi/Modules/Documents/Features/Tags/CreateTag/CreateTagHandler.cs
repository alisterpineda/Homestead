using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.CreateTag;

public class CreateTagHandler(ITagPathResolver tagPathResolver, ICurrentUserAccessor currentUser, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<CreateTagResponse> Handle(CreateTagCommand request, CancellationToken ct)
    {
        var tag = await tagPathResolver.ResolveOrCreateAsync(request.Path, leafColor: request.Color, leafDescription: request.Description, createdByUserId: currentUser.UserId, ct: ct);

        audit.Created("Tag", tag.Id);
        await unitOfWork.SaveChangesAsync(ct);

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
