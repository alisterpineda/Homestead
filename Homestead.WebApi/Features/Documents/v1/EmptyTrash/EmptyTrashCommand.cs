using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.EmptyTrash;

public record EmptyTrashCommand : IRequest<EmptyTrashResponse>;
