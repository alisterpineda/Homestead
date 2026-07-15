using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

public record RollbackStatementImportCommand : ICommand<RollbackStatementImportResponse>
{
    public required Guid SourceDocumentVersionId { get; init; }
}

public record RollbackStatementImportResponse
{
    public required int DeletedCount { get; init; }
}
