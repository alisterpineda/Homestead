using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngineSummaries.GetTextExtractionEngineSummaries;

public class GetTextExtractionEngineSummariesQuery : IRequest<IReadOnlyList<TextExtractionEngineSummary>>;
