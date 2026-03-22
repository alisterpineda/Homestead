using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public record BulkDeleteTextExtractionEnginesRequest(List<Guid> Ids) : IRequest<BulkDeleteTextExtractionEnginesResponse>;
