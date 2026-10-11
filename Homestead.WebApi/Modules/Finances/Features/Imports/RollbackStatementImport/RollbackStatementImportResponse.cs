namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

public record RollbackStatementImportResponse
{
    public required int DeletedCount { get; init; }
}
