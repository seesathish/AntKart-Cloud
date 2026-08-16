using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AK.Payments.Infrastructure.Persistence.Repositories;

internal sealed class SavedCardRepository(PaymentsDbContext db) : ISavedCardRepository
{
    public async Task<IReadOnlyList<SavedCard>> GetByUserIdAsync(string userId, CancellationToken ct = default)
        => await db.SavedCards.Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public Task<SavedCard?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.SavedCards.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<SavedCard?> GetByTokenAsync(string tokenId, CancellationToken ct = default)
        => db.SavedCards.FirstOrDefaultAsync(c => c.RazorpayTokenId == tokenId, ct);

    public async Task AddAsync(SavedCard card, CancellationToken ct = default)
        => await db.SavedCards.AddAsync(card, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var card = await db.SavedCards.FindAsync([id], ct);
        if (card is not null) db.SavedCards.Remove(card);
    }
}
