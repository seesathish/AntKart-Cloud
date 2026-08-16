using AK.Order.Application.Sagas;
using AK.Order.Domain.Entities;
using AK.Order.Infrastructure.Persistence.Configurations;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Infrastructure.Persistence;

// EF Core DbContext for the Order service.
// All tables for this service live in the same PostgreSQL database (AKOrdersDb).
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    // SAGA state table — MassTransit reads/writes one row per in-flight order.
    public DbSet<OrderSagaState> OrderSagaStates => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply fluent API configurations (column names, constraints, owned types).
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new OrderSagaStateMap());

        // MassTransit Outbox pattern — adds three extra tables:
        //   InboxState:    deduplicates incoming messages (prevents reprocessing on retry)
        //   OutboxMessage: stores integration events to publish atomically with DB changes
        //   OutboxState:   tracks which outbox messages have been delivered
        //
        // Why? Without the outbox, saving an order and publishing an event are two separate
        // operations. If the service crashes between them, the event is lost. The outbox
        // writes the event to the same DB transaction as the business data, so they succeed
        // or fail together. A background process then picks up and delivers the events.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
