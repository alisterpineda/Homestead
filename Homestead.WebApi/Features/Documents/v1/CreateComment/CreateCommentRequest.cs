using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public record CreateCommentRequest(string Body) : IRequest<CreateCommentResponse>
{
    internal Guid DocumentId { get; init; }
}
