using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.CreateTag;

public record CreateTagRequest(string Path, string? Color = null, string? Description = null) : IRequest<CreateTagResponse>;
