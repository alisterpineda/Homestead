using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.RestoreDocuments;

public record RestoreDocumentsRequest(List<Guid> Ids) : IRequest<RestoreDocumentsResponse>;
