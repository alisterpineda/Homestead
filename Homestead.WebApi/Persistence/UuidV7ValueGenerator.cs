using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Homestead.WebApi.Persistence;

/// <summary>
/// Generates RFC 9562 UUIDv7 primary keys. They are time-ordered, so new rows append at the right edge of
/// the PostgreSQL B-tree instead of landing on a random leaf page the way v4 <see cref="Guid.NewGuid"/>
/// keys do. Registered once for every Guid primary key in <see cref="AppDbContext.OnModelCreating"/>;
/// entities leave <c>Id</c> unset and EF assigns it when the entity is tracked (at <c>Add</c>, not at
/// <c>SaveChanges</c>), so <c>entity.Id</c> is safe to read right after <c>db.Set.Add(entity)</c>.
/// Old v4 rows coexist with v7 rows in the same column; nothing may rely on key order — sort by
/// <c>CreatedAt</c> and treat the ordering purely as an index-locality optimisation.
/// </summary>
public sealed class UuidV7ValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
