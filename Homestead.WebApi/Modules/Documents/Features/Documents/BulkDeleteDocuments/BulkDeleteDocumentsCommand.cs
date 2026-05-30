using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkDeleteDocuments;

public record BulkDeleteDocumentsCommand(List<Guid> Ids) : ICommand<BulkDeleteDocumentsResponse>;
