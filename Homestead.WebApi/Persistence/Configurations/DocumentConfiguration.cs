using Homestead.WebApi.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", "documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(d => d.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(d => d.UpdatedByUser)
            .WithMany()
            .HasForeignKey(d => d.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(d => d.Summary);

        builder.Property(d => d.DocumentDate);
        builder.Property(d => d.ExpiryDate);
        builder.Property(d => d.Notes);

        builder.HasQueryFilter(d => d.DeletedAt == null);

        builder.HasOne(d => d.DeletedByUser)
            .WithMany()
            .HasForeignKey(d => d.DeletedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.CreatedByUser)
            .WithMany()
            .HasForeignKey(d => d.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.OwnerUser)
            .WithMany()
            .HasForeignKey(d => d.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.Group)
            .WithMany()
            .HasForeignKey(d => d.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.GroupId);

        var searchVector = builder.Property(d => d.SearchVector)
            .HasColumnType("tsvector");

        // SearchVector is maintained entirely by DB triggers (see AddSearchVectorTriggers). EF must
        // never write it — on insert or update — otherwise a save that doesn't touch Name/Summary could
        // overwrite the trigger-computed value with null (the BEFORE trigger only fires for Name/Summary).
        searchVector.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        searchVector.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(d => d.SearchVector)
            .HasMethod("GIN");

        builder.HasIndex(d => d.CreatedAt)
            .IsDescending(true);

        builder.HasIndex(d => d.UpdatedAt)
            .IsDescending(true);

        builder.HasIndex(d => d.DeletedAt);

        builder.HasIndex(d => d.CreatedByUserId);
        builder.HasIndex(d => d.OwnerUserId);
    }
}
