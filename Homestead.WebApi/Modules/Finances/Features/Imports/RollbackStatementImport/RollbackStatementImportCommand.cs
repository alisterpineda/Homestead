namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

public record RollbackStatementImportCommand
{
    public required Guid DocumentId { get; init; }
}

public record RollbackStatementImportResponse
{
    public required int DeletedCount { get; init; }
}
