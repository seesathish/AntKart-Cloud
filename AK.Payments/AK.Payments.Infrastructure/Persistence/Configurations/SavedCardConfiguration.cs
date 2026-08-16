using AK.Payments.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AK.Payments.Infrastructure.Persistence.Configurations;

public sealed class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.ToTable("saved_cards");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.RazorpayCustomerId).IsRequired().HasMaxLength(50);
        builder.Property(c => c.RazorpayTokenId).IsRequired().HasMaxLength(50);
        builder.Property(c => c.CardNetwork).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Last4).IsRequired().HasMaxLength(4);
        builder.Property(c => c.CardType).IsRequired().HasMaxLength(20);
        builder.Property(c => c.CardName).IsRequired().HasMaxLength(100);
        builder.Property(c => c.IsDefault).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.RazorpayTokenId).IsUnique();

        builder.Ignore(c => c.DomainEvents);
    }
}
