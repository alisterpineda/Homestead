namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateParty;

public record UpdatePartyRequest
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}
