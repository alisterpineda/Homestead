using Homestead.WebApi.Domains.Documents;

namespace Homestead.WebApi.Persistence;

public interface ITagPathResolver
{
    /// <summary>
    /// Resolves a slash-delimited tag path, creating any missing segments.
    /// Entities are added to the change tracker but not persisted — the caller
    /// must call <c>SaveChangesAsync</c>.
    /// </summary>
    Task<Tag> ResolveOrCreateAsync(string path, string? leafColor = null, Guid? createdByUserId = null, CancellationToken ct = default);
}
