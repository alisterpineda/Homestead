namespace Homestead.WebApi.Features.Documents;

public interface ISearchVectorUpdater
{
    Task UpdateSearchVectorAsync(Guid documentId, CancellationToken ct);
}
