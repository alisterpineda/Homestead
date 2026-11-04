using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Inventory;
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
    public DbSet<Domain.Documents.Tag> Tags { get; set; }
    public DbSet<DocumentTag> DocumentTags { get; set; }
    public DbSet<Party> Parties { get; set; }
    public DbSet<DocumentParty> DocumentParties { get; set; }
    public DbSet<ProcessingJob> ProcessingJobs { get; set; }
    public DbSet<ProcessingSchedule> ProcessingSchedules { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<DocumentSuggestion> DocumentSuggestions { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }

    public DbSet<BackupTargetState> BackupTargetStates { get; set; }
    public DbSet<BackupSource> BackupSources { get; set; }
    public DbSet<DocumentBackupState> DocumentBackupStates { get; set; }
    public DbSet<BackupTargetFolder> BackupTargetFolders { get; set; }
    public DbSet<BackupUploadSession> BackupUploadSessions { get; set; }

    public DbSet<Commodity> Commodities { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<AccountTransaction> AccountTransactions { get; set; }
    public DbSet<AccountTransactionSplit> AccountTransactionSplits { get; set; }
    public DbSet<TransactionDocumentLink> TransactionDocumentLinks { get; set; }
    public DbSet<CommodityPrice> CommodityPrices { get; set; }
    public DbSet<CommodityPriceSourceSettings> CommodityPriceSourceSettings { get; set; }
    public DbSet<Payee> Payees { get; set; }
    public DbSet<PayeeMapping> PayeeMappings { get; set; }

    public DbSet<Area> Areas { get; set; }
    public DbSet<Brand> Brands { get; set; }
    public DbSet<Item> Items { get; set; }
    public DbSet<StockEntry> StockEntries { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<Domain.Inventory.Tag> InventoryTags { get; set; }
    public DbSet<Domain.Inventory.ItemTag> ItemTags { get; set; }
    public DbSet<ItemNote> ItemNotes { get; set; }
    public DbSet<ItemWarranty> ItemWarranties { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        // Accent folding for document search: the stored search vector and every query side fold through it.
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        UseUuidV7PrimaryKeys(modelBuilder);
    }

    /// <summary>
    /// Every single-column Guid primary key that the app mints itself gets <see cref="UuidV7ValueGenerator"/>.
    /// Runs after the per-entity configurations so a new entity picks it up without any per-config line.
    /// Skipped: derived TPH types (the root owns the key), and keys that are also foreign keys (shared
    /// primary keys copy the principal's value, they are never generated).
    /// </summary>
    private static void UseUuidV7PrimaryKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is not null) continue;

            var key = entityType.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1) continue;

            var property = key.Properties[0];
            if (property.ClrType != typeof(Guid) || property.IsForeignKey()) continue;

            modelBuilder.Entity(entityType.ClrType)
                .Property(property.Name)
                .ValueGeneratedOnAdd()
                .HasValueGenerator<UuidV7ValueGenerator>();
        }
    }
}
