using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Domain.Settings;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AppUser> Users { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentVersion> DocumentVersions { get; set; }
    public DbSet<DocumentComment> DocumentComments { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<DocumentTag> DocumentTags { get; set; }
    public DbSet<Party> Parties { get; set; }
    public DbSet<DocumentParty> DocumentParties { get; set; }
    public DbSet<ProcessingJob> ProcessingJobs { get; set; }
    public DbSet<ProcessingSchedule> ProcessingSchedules { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<TextExtractionEngine> TextExtractionEngines { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }

    public DbSet<Commodity> Commodities { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<AccountTransaction> AccountTransactions { get; set; }
    public DbSet<AccountTransactionSplit> AccountTransactionSplits { get; set; }
    public DbSet<CommodityPrice> CommodityPrices { get; set; }
    public DbSet<CommodityPriceSourceSettings> CommodityPriceSourceSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
