namespace AK.Order.Application.Common.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IOrderRepository Orders { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
