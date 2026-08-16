using AK.Payments.Domain.Entities;

namespace AK.Payments.Application.Common.Interfaces;

public interface ISavedCardRepository
{
    Task<IReadOnlyList<SavedCard>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<SavedCard?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SavedCard?> GetByTokenAsync(string tokenId, CancellationToken ct = default);
    Task AddAsync(SavedCard card, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
