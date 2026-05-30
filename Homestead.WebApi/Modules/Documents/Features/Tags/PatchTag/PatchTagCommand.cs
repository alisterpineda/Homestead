using MediatR;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.PatchTag;

public class PatchTagModel
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
}

public record PatchTagCommand : ICommand<PatchTagResponse>
{
    public Guid Id { get; init; }
    public required JsonPatchDocument<PatchTagModel> PatchDocument { get; init; }
}
