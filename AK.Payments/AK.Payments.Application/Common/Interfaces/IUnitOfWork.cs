namespace AK.Payments.Application.Common.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IPaymentRepository Payments { get; }
    ISavedCardRepository SavedCards { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
