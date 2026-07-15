using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.GetStatementImport;

// Read-only (intentionally not ICommand — no transaction).
public record GetStatementImportQuery(Guid JobId) : IRequest<StatementImportResponse>;
