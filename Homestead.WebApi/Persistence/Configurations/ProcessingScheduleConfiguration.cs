using Homestead.WebApi.Domain.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class ProcessingScheduleConfiguration : IEntityTypeConfiguration<ProcessingSchedule>
{
    public void Configure(EntityTypeBuilder<ProcessingSchedule> builder)
    {
        builder.ToTable("schedules", "processing");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .IsRequired();

        builder.Property(s => s.JobType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.Payload)
            .HasColumnType("jsonb");

        builder.Property(s => s.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.CronExpression)
            .IsRequired();

        builder.Property(s => s.TimeZone)
            .IsRequired()
            .HasDefaultValue("Etc/UTC");

        builder.Property(s => s.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.NextRunAt)
            .IsRequired();

        builder.HasOne<ProcessingJob>()
            .WithMany()
            .HasForeignKey(s => s.LastJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.IsEnabled, s.NextRunAt });
        builder.HasIndex(s => s.JobType);
        builder.HasIndex(s => s.CreatedByUserId);
    }
}
