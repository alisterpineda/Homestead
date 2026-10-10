namespace Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;

public record CreateCommentCommand(string Body)
{
    internal Guid DocumentId { get; init; }
}
