namespace Homestead.WebApi.Modules.Finances.Features.Imports.GetStatementImport;

// Read-only (served over GET — no transaction).
public record GetStatementImportQuery(Guid JobId);
