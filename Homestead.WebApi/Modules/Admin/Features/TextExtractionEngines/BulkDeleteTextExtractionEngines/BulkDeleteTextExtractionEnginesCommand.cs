using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public record BulkDeleteTextExtractionEnginesCommand(List<Guid> Ids) : ICommand<BulkDeleteTextExtractionEnginesResponse>;
