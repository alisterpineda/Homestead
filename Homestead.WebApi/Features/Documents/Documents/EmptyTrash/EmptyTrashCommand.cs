using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.EmptyTrash;

public record EmptyTrashCommand : IRequest<EmptyTrashResponse>;
