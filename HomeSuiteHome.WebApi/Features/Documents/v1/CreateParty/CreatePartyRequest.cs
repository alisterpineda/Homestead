namespace HomeSuiteHome.WebApi.Features.Documents.v1.CreateParty;

public record CreatePartyRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
