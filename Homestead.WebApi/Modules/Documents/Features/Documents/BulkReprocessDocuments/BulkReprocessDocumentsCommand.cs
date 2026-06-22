using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;

public record BulkReprocessDocumentsCommand(
    List<Guid> Ids) : ICommand<BulkReprocessDocumentsResponse>;
