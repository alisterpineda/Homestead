using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.EmptyTrash;

public record EmptyTrashCommand : IRequest<EmptyTrashResponse>;
