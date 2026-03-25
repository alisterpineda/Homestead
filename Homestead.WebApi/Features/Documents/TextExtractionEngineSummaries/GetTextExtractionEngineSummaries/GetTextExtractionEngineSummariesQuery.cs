using MediatR;

namespace Homestead.WebApi.Features.Documents.TextExtractionEngineSummaries.GetTextExtractionEngineSummaries;

public class GetTextExtractionEngineSummariesQuery : IRequest<IReadOnlyList<TextExtractionEngineSummary>>;
