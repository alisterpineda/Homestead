namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.BulkDeleteOcrEngines;

public record BulkDeleteOcrEnginesRequest(List<Guid> Ids);
