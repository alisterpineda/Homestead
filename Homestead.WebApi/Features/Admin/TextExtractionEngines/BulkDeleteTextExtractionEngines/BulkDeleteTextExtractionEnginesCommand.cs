using MediatR;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public record BulkDeleteTextExtractionEnginesCommand(List<Guid> Ids) : IRequest<BulkDeleteTextExtractionEnginesResponse>;
