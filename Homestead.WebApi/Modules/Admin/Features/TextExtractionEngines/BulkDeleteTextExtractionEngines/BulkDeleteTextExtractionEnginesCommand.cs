using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public record BulkDeleteTextExtractionEnginesCommand(List<Guid> Ids) : IRequest<BulkDeleteTextExtractionEnginesResponse>;
