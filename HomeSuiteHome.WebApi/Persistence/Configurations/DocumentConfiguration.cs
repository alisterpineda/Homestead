using HomeSuiteHome.WebApi.Domains.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeSuiteHome.WebApi.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", "documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
            .IsRequired();
    }
}