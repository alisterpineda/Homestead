namespace Homestead.WebApi.Modules.Finances.Features.Imports.InitiateStatementImport;

public record InitiateStatementImportCommand
{
    public required Guid AccountId { get; init; }
    public required Guid DocumentVersionId { get; init; }
}

public record InitiateStatementImportResponse
{
    public required Guid JobId { get; init; }
}
