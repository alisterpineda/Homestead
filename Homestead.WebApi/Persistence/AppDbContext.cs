using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Domains.Processing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid,
        IdentityUserClaim<Guid>, AppUserRole, IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>(options)
{
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentVersion> DocumentVersions { get; set; }
    public DbSet<DocumentComment> DocumentComments { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<DocumentTag> DocumentTags { get; set; }
    public DbSet<Party> Parties { get; set; }
    public DbSet<DocumentParty> DocumentParties { get; set; }
    public DbSet<ProcessingJob> ProcessingJobs { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
