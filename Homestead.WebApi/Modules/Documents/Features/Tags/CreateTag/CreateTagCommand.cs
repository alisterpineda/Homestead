using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.CreateTag;

public record CreateTagCommand(string Path, string? Color = null, string? Description = null) : ICommand<CreateTagResponse>;
