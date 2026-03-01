namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public record CreateCommentResponse
{
    public required Guid Id { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
}
