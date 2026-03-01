namespace Homestead.WebApi.Persistence;

public interface ISearchVectorUpdater
{
    Task UpdateSearchVectorAsync(Guid documentId, CancellationToken ct);
}
