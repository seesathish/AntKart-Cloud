using AK.Order.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.OrderNumber).IsRequired().HasMaxLength(30);
        builder.Property(o => o.UserId).IsRequired().HasMaxLength(100);
        builder.Property(o => o.CustomerEmail).IsRequired().HasMaxLength(256).HasDefaultValue(string.Empty);
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.CreatedAt).IsRequired();
        // UpdatedAt is nullable: inherited from AK.BuildingBlocks.DDD.Entity, set only after mutation.
        builder.Property(o => o.UpdatedAt);

        builder.OwnsOne(o => o.ShippingAddress, addr =>
        {
            addr.Property(a => a.FullName).IsRequired().HasMaxLength(200).HasColumnName("ShipFullName");
            addr.Property(a => a.AddressLine1).IsRequired().HasMaxLength(500).HasColumnName("ShipAddressLine1");
            addr.Property(a => a.AddressLine2).HasMaxLength(500).HasColumnName("ShipAddressLine2");
            addr.Property(a => a.City).IsRequired().HasMaxLength(100).HasColumnName("ShipCity");
            addr.Property(a => a.State).IsRequired().HasMaxLength(100).HasColumnName("ShipState");
            addr.Property(a => a.PostalCode).IsRequired().HasMaxLength(20).HasColumnName("ShipPostalCode");
            addr.Property(a => a.Country).IsRequired().HasMaxLength(100).HasColumnName("ShipCountry");
            addr.Property(a => a.Phone).IsRequired().HasMaxLength(30).HasColumnName("ShipPhone");
        });

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(OrderEntity.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.UserId);

        builder.Ignore(o => o.TotalAmount);
        builder.Ignore(o => o.TotalItems);
        builder.Ignore(o => o.DomainEvents);
    }
}
