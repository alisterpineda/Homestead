using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public record BulkDeleteTextExtractionEnginesCommand(List<Guid> Ids) : IRequest<BulkDeleteTextExtractionEnginesResponse>;
