using AK.Order.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AK.Order.Infrastructure.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.ProductId).IsRequired().HasMaxLength(100);
        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.SKU).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Price).HasColumnType("decimal(18,2)");
        builder.Property(i => i.Quantity).IsRequired();
        builder.Property(i => i.ImageUrl).HasMaxLength(1000);

        builder.Ignore(i => i.SubTotal);
        builder.Ignore(i => i.OrderId);
    }
}
