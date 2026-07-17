using System.Security.Cryptography;
using System.Text;

namespace Homestead.WebApi.Domain.Auditing;

/// <summary>
/// Deterministic Guid from a stable key string — the audit EntityId for entities whose real key
/// is not a single Guid (string keys, composite keys). Callers own their key format. Changing the
/// hash algorithm de-correlates every previously written audit row for those entities, so don't.
/// </summary>
public static class DeterministicGuid
{
    public static Guid FromKey(string key) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(key)));
}
