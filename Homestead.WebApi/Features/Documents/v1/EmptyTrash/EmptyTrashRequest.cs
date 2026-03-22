using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.EmptyTrash;

public record EmptyTrashRequest : IRequest<EmptyTrashResponse>;
