using HomeSuiteHome.WebApi.Domains.Documents;
using Microsoft.EntityFrameworkCore;

namespace HomeSuiteHome.WebApi.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}