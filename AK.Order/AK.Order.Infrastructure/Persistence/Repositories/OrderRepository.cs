using AK.Order.Application.Common.Interfaces;
using AK.Order.Domain.Common;
using Microsoft.EntityFrameworkCore;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(OrderDbContext db) : IOrderRepository
{
    public async Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<OrderEntity?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default) =>
        await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, ct);

    public async Task<IReadOnlyList<OrderEntity>> GetByUserIdAsync(string userId, CancellationToken ct = default) =>
        await db.Orders.Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OrderEntity>> ListAsync(ISpecification<OrderEntity> spec, CancellationToken ct = default)
    {
        var query = ApplySpecification(spec);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CountAsync(ISpecification<OrderEntity> spec, CancellationToken ct = default)
    {
        var query = db.Orders.Include(o => o.Items).AsQueryable();
        query = query.Where(spec.Criteria);
        return await query.CountAsync(ct);
    }

    public async Task<OrderEntity> AddAsync(OrderEntity order, CancellationToken ct = default)
    {
        await db.Orders.AddAsync(order, ct);
        return order;
    }

    public Task UpdateAsync(OrderEntity order, CancellationToken ct = default)
    {
        db.Orders.Update(order);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var order = await db.Orders.FindAsync([id], ct);
        if (order is not null) db.Orders.Remove(order);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        await db.Orders.AnyAsync(o => o.Id == id, ct);

    private IQueryable<OrderEntity> ApplySpecification(ISpecification<OrderEntity> spec)
    {
        var query = db.Orders.Include(o => o.Items).AsQueryable();

        query = query.Where(spec.Criteria);

        foreach (var include in spec.Includes)
            query = query.Include(include);

        if (spec.OrderBy is not null)
            query = query.OrderBy(spec.OrderBy);
        else if (spec.OrderByDescending is not null)
            query = query.OrderByDescending(spec.OrderByDescending);

        if (spec.Skip.HasValue)
            query = query.Skip(spec.Skip.Value);

        if (spec.Take.HasValue)
            query = query.Take(spec.Take.Value);

        return query;
    }
}
