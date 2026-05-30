using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.EmptyTrash;

public record EmptyTrashCommand : ICommand<EmptyTrashResponse>;
