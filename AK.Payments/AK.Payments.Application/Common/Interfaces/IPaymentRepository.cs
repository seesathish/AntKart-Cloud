using AK.Payments.Domain.Entities;

namespace AK.Payments.Application.Common.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task AddAsync(Payment payment, CancellationToken ct = default);
}
