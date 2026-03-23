using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.RestoreDocuments;

public record RestoreDocumentsCommand(List<Guid> Ids) : IRequest<RestoreDocumentsResponse>;
