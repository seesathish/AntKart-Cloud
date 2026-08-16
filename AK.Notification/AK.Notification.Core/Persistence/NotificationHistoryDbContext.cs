using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AK.Notification.Core.Persistence;

// EF Core context for the notification history/audit trail. A single table; enum columns are
// stored as readable strings. Connection is configured (secret-less) in AddNotificationCore.
public sealed class NotificationHistoryDbContext(DbContextOptions<NotificationHistoryDbContext> options)
    : DbContext(options)
{
    public DbSet<NotificationHistory> NotificationHistory => Set<NotificationHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new NotificationHistoryConfiguration());
}

internal sealed class NotificationHistoryConfiguration : IEntityTypeConfiguration<NotificationHistory>
{
    public void Configure(EntityTypeBuilder<NotificationHistory> builder)
    {
        builder.ToTable("notification_history");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.NotificationType).IsRequired().HasConversion<string>().HasMaxLength(64);
        builder.Property(h => h.Recipient).IsRequired().HasMaxLength(512);
        builder.Property(h => h.ChannelType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.CorrelationId).HasMaxLength(128);
        builder.Property(h => h.ErrorMessage).HasMaxLength(2048);
        builder.Property(h => h.CreatedAt).IsRequired();
        builder.Property(h => h.UpdatedAt);

        builder.HasIndex(h => h.CorrelationId);
        builder.HasIndex(h => h.CreatedAt);
    }
}
