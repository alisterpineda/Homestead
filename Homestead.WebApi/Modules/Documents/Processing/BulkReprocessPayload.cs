namespace Homestead.WebApi.Modules.Documents.Processing;

public record BulkReprocessPayload(List<Guid> DocumentIds, Guid? TextExtractionEngineId);
