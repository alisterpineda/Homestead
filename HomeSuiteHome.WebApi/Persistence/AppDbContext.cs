using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Domains.Identity;
using HomeSuiteHome.WebApi.Domains.Parties;
using HomeSuiteHome.WebApi.Domains.Processing;
using HomeSuiteHome.WebApi.Domains.Tags;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<DocumentTag> DocumentTags { get; set; }
    public DbSet<Party> Parties { get; set; }
    public DbSet<DocumentParty> DocumentParties { get; set; }
    public DbSet<AppUser> AppUsers { get; set; }
    public DbSet<ProcessingJob> ProcessingJobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}