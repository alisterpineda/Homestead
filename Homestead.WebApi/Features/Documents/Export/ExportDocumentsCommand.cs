using MediatR;

namespace Homestead.WebApi.Features.Documents.Export;

public record ExportDocumentsCommand(List<Guid>? Ids) : IRequest<ExportDocumentsResponse>;
