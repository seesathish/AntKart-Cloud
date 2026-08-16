using AK.Payments.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AK.Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.OrderId).IsRequired();
        builder.Property(p => p.UserId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.CustomerEmail).IsRequired().HasMaxLength(256).HasDefaultValue(string.Empty);
        builder.Property(p => p.CustomerName).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(p => p.OrderNumber).IsRequired().HasMaxLength(30).HasDefaultValue(string.Empty);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(10).HasDefaultValue("INR");
        builder.Property(p => p.Status).IsRequired();
        builder.Property(p => p.Method).IsRequired();
        builder.Property(p => p.RazorpayOrderId).HasMaxLength(50);
        builder.Property(p => p.RazorpayPaymentId).HasMaxLength(50);
        builder.Property(p => p.RazorpaySignature).HasMaxLength(200);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.SavedCardToken).HasMaxLength(50);
        builder.Property(p => p.CreatedAt).IsRequired();
        // UpdatedAt is nullable: set only after the first mutation (base Entity pattern).
        builder.Property(p => p.UpdatedAt);

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.UserId);

        builder.Ignore(p => p.DomainEvents);
    }
}
