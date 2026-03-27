namespace Homestead.WebApi.Features.Documents.Processing;

public record BulkReprocessPayload(List<Guid> DocumentIds, Guid? TextExtractionEngineId);
