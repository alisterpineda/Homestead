using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;

// EngineKey is the caller's explicit pick; null means the household default. It is resolved once at
// enqueue and stamped into the parent payload, so every child of the fan-out runs the same engine.
public record BulkReprocessDocumentsCommand(
    List<Guid> Ids,
    string? EngineKey = null) : ICommand<BulkReprocessDocumentsResponse>;
