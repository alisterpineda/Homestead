using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Services;

public interface ITagPathResolver
{
    /// <summary>
    /// Resolves a slash-delimited tag path, creating any missing segments.
    /// Entities are added to the change tracker but not persisted — the caller
    /// must call <c>SaveChangesAsync</c>.
    /// </summary>
    Task<Tag> ResolveOrCreateAsync(string path, string? leafColor = null, string? leafDescription = null, Guid? createdByUserId = null, CancellationToken ct = default);

    /// <summary>
    /// Resolves multiple slash-delimited tag paths in a single batch, creating any missing segments.
    /// All candidate paths are fetched in a single DB query.
    /// Entities are added to the change tracker but not persisted — the caller
    /// must call <c>SaveChangesAsync</c>.
    /// </summary>
    Task<List<Tag>> ResolveOrCreateManyAsync(List<string> paths, Guid? createdByUserId = null, CancellationToken ct = default);
}
