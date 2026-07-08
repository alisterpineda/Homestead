using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Inventory.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.CreateTag;

public class CreateTagHandler(
    IInventoryTagPathResolver tagPathResolver, ICurrentUserAccessor currentUser, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateInventoryTagCommand, CreateInventoryTagResponse>
{
    public async Task<CreateInventoryTagResponse> Handle(CreateInventoryTagCommand request, CancellationToken ct)
    {
        var tag = await tagPathResolver.ResolveOrCreateAsync(request.Path, leafColor: request.Color, leafDescription: request.Description, createdByUserId: currentUser.UserId, ct: ct);

        audit.Created("InventoryTag", tag.Id);
        await unitOfWork.SaveChangesAsync(ct);

        return new CreateInventoryTagResponse
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
