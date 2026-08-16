using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AK.Payments.Infrastructure.Persistence.Repositories;

internal sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default)
        => db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

    public async Task<IReadOnlyList<Payment>> GetByUserIdAsync(string userId, CancellationToken ct = default)
        => await db.Payments.Where(p => p.UserId == userId).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(Payment payment, CancellationToken ct = default)
        => await db.Payments.AddAsync(payment, ct);
}
