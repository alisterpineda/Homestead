namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public record CreateCommentRequest(string Body)
{
    internal Guid DocumentId { get; init; }
}
