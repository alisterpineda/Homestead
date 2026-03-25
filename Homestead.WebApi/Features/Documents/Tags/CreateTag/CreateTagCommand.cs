using MediatR;

namespace Homestead.WebApi.Features.Documents.Tags.CreateTag;

public record CreateTagCommand(string Path, string? Color = null, string? Description = null) : IRequest<CreateTagResponse>;
