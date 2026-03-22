using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetTextExtractionEngineSummaries;

public class GetTextExtractionEngineSummariesRequest : IRequest<IReadOnlyList<TextExtractionEngineSummary>>;
