using AK.Order.Domain.Common;
using OrderEntity = AK.Order.Domain.Entities.Order;

namespace AK.Order.Application.Common.Interfaces;

public interface IOrderRepository
{
    Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<OrderEntity?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default);
    Task<IReadOnlyList<OrderEntity>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderEntity>> ListAsync(ISpecification<OrderEntity> spec, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<OrderEntity> spec, CancellationToken ct = default);
    Task<OrderEntity> AddAsync(OrderEntity order, CancellationToken ct = default);
    Task UpdateAsync(OrderEntity order, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
