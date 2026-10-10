namespace Homestead.WebApi.Modules.Documents.Features.Tags.CreateTag;

public record CreateTagCommand(string Path, string? Color = null, string? Description = null);
