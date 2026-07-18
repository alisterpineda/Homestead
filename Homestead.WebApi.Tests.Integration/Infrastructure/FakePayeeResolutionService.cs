using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Deterministic stand-in for <see cref="IPayeeResolutionService"/> (the AI tier-②/③ pass) so the import
/// spine never makes a live model call. Returns no suggestions — the learning-lifecycle tests exercise the
/// deterministic tier-① path, so the AI pass must stay a silent no-op rather than resolving anything.
/// </summary>
public sealed class FakePayeeResolutionService : IPayeeResolutionService
{
    public Task<IReadOnlyDictionary<int, PayeeResolution>> ResolveAsync(
        IReadOnlyList<PayeeResolutionInput> lines,
        IReadOnlyList<PayeeCatalogEntry> catalog,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<int, PayeeResolution>>(new Dictionary<int, PayeeResolution>());
}
