using AK.Order.Application.Common.Interfaces;
using AK.Order.Infrastructure.Persistence.Repositories;

namespace AK.Order.Infrastructure.Persistence;

// Unit of Work: groups all repository operations for a single request into one transaction.
// Handlers call uow.Orders.Add/Update, then uow.SaveChangesAsync() to commit everything in one go.
// This prevents partial writes where, for example, an order is saved but its items are not.
//
// The lazy-initialised _orders field means the repository is only created if the handler
// actually uses it — avoids unnecessary allocations for read-only queries.
internal sealed class UnitOfWork(OrderDbContext db) : IUnitOfWork
{
    private IOrderRepository? _orders;

    // Lazy init — repository shares the same DbContext instance so changes are tracked together.
    public IOrderRepository Orders => _orders ??= new OrderRepository(db);

    // Flushes all tracked changes to PostgreSQL in a single transaction.
    // Also triggers the MassTransit Outbox delivery of any queued integration events.
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);

    public void Dispose() => db.Dispose();
}
