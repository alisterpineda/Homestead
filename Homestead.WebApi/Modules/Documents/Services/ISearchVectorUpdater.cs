namespace Homestead.WebApi.Modules.Documents.Services;

public interface ISearchVectorUpdater
{
    Task UpdateSearchVectorAsync(Guid documentId, CancellationToken ct);
}
