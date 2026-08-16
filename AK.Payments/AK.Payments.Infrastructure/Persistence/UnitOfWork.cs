using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Infrastructure.Persistence.Repositories;

namespace AK.Payments.Infrastructure.Persistence;

internal sealed class UnitOfWork(PaymentsDbContext db) : IUnitOfWork
{
    public IPaymentRepository Payments { get; } = new PaymentRepository(db);
    public ISavedCardRepository SavedCards { get; } = new SavedCardRepository(db);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public void Dispose() => db.Dispose();
}
