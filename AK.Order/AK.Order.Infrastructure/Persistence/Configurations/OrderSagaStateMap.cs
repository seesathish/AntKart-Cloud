using AK.Order.Application.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AK.Order.Infrastructure.Persistence.Configurations;

public sealed class OrderSagaStateMap : IEntityTypeConfiguration<OrderSagaState>
{
    public void Configure(EntityTypeBuilder<OrderSagaState> builder)
    {
        builder.ToTable("order_saga_states");
        builder.HasKey(x => x.CorrelationId);
        builder.Property(x => x.CurrentState).HasMaxLength(64);
        builder.Property(x => x.UserId).HasMaxLength(256);
        builder.Property(x => x.CustomerEmail).HasMaxLength(256).HasDefaultValue(string.Empty);
        builder.Property(x => x.CustomerName).HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(x => x.OrderNumber).HasMaxLength(30).HasDefaultValue(string.Empty);
        builder.Property(x => x.TotalAmount).HasDefaultValue(0m);
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
