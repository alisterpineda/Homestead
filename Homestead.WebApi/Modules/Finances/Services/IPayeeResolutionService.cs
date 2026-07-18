namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// The AI second-pass that proposes payees for statement lines the deterministic tier-① learning table
/// couldn't resolve. A <b>separate, small, non-streaming</b> call (the extraction call stays streaming to
/// survive long statements). Per line it returns either an existing catalog payee (tier ②) or a cleaned new
/// payee name (tier ③) — or nothing. Output is hint-only: the caller stages it as a ghost that never
/// auto-applies and never teaches. Callers gate on AI being configured; this service assumes it is.
/// </summary>
public interface IPayeeResolutionService
{
    /// <summary>
    /// Resolve payee suggestions for the given lines against the payee <paramref name="catalog"/>. Returns a
    /// map keyed by the input line index (only lines that got a suggestion appear). A resolution is either an
    /// existing payee (tier ②) or a proposed new name (tier ③).
    /// </summary>
    Task<IReadOnlyDictionary<int, PayeeResolution>> ResolveAsync(
        IReadOnlyList<PayeeResolutionInput> lines,
        IReadOnlyList<PayeeCatalogEntry> catalog,
        CancellationToken ct);
}

/// <summary>One line to resolve — its stable proposal index and raw description.</summary>
public record PayeeResolutionInput(int LineIndex, string Description);

/// <summary>A candidate existing payee the model may match against, referenced by position.</summary>
public record PayeeCatalogEntry(Guid Id, string Name);

/// <summary>
/// One resolved suggestion: exactly one of <see cref="ExistingPayeeId"/> (tier ②) or
/// <see cref="NewPayeeName"/> (tier ③) is set.
/// </summary>
public record PayeeResolution(Guid? ExistingPayeeId, string? NewPayeeName);
