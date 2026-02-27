namespace Homestead.WebApi.Features.Documents.v1.UpdateTag;

public record UpdateTagRequest
{
    public string? Color { get; init; }
}

public record UpdateTagCommand
{
    public Guid Id { get; init; }
    public string? Color { get; init; }
}
