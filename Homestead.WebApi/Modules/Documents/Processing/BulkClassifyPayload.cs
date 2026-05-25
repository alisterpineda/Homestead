namespace Homestead.WebApi.Modules.Documents.Processing;

public record BulkClassifyPayload(List<Guid> DocumentIds, Guid? AiProviderId = null);
