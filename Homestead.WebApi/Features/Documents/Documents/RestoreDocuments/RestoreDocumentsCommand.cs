using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.RestoreDocuments;

public record RestoreDocumentsCommand(List<Guid> Ids) : IRequest<RestoreDocumentsResponse>;
