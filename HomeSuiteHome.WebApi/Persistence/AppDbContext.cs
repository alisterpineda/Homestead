using HomeSuiteHome.WebApi.Domains.Documents;
using HomeSuiteHome.WebApi.Domains.Tags;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<DocumentTag> DocumentTags { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}