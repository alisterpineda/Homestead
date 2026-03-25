using Homestead.WebApi.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("roles", "identity");

        // Role names must match AppRole enum. HasData requires static literals (no nameof/ToUpper).
        builder.HasData(
            new IdentityRole<Guid> { Id = new Guid("36dd9cfb-c763-4e7a-b6ad-fb2894387100"), Name = "Admin", NormalizedName = "ADMIN", ConcurrencyStamp = "b026694e-34ae-4a7e-b94e-1b0566351474" },
            new IdentityRole<Guid> { Id = new Guid("470b20ff-ebd9-4704-9412-5d4f7268e0b4"), Name = "Contributor", NormalizedName = "CONTRIBUTOR", ConcurrencyStamp = "a1e03693-1592-4c68-a12c-51e4e6e1bf6e" },
            new IdentityRole<Guid> { Id = new Guid("6cb8bf9d-1af1-442a-b077-d337e83dcae3"), Name = "Viewer", NormalizedName = "VIEWER", ConcurrencyStamp = "dc457fa2-09f0-4c65-a7b5-963327cb41a4" },
            new IdentityRole<Guid> { Id = new Guid("2dfd3ea9-d87c-4772-ac15-58d639c67fe1"), Name = "Guest", NormalizedName = "GUEST", ConcurrencyStamp = "e5a3a534-6cb0-4e8f-b7a1-3f2c6e9d04a7" });
    }
}

public class AppUserRoleConfiguration : IEntityTypeConfiguration<AppUserRole>
{
    public void Configure(EntityTypeBuilder<AppUserRole> builder)
    {
        builder.ToTable("user_roles", "identity");

        builder.Property(r => r.Source)
            .HasConversion<string>()
            .HasDefaultValue(RoleSource.Local)
            .IsRequired();
    }
}

public class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
        => builder.ToTable("user_claims", "identity");
}

public class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
        => builder.ToTable("user_logins", "identity");
}

public class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
        => builder.ToTable("user_tokens", "identity");
}

public class IdentityRoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder)
        => builder.ToTable("role_claims", "identity");
}
