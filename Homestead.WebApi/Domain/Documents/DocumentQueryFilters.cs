namespace Homestead.WebApi.Domain.Documents;

/// <summary>Names for <see cref="Document"/>'s global query filters (EF Core 10 named filters).</summary>
public static class DocumentQueryFilters
{
    /// <summary>
    /// The trash filter (<c>DeletedAt == null</c>) configured in DocumentConfiguration. Lift it
    /// with <c>IgnoreQueryFilters([SoftDelete])</c> — never the parameterless overload, which
    /// would also lift any filter added to <see cref="Document"/> later.
    /// </summary>
    public const string SoftDelete = "SoftDelete";
}
