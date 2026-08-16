using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Application.Mapping;
using MediatR;

namespace AK.Payments.Application.Queries.GetUserSavedCards;

public sealed class GetUserSavedCardsQueryHandler(IUnitOfWork uow) : IRequestHandler<GetUserSavedCardsQuery, IReadOnlyList<SavedCardDto>>
{
    public async Task<IReadOnlyList<SavedCardDto>> Handle(GetUserSavedCardsQuery request, CancellationToken ct)
    {
        var cards = await uow.SavedCards.GetByUserIdAsync(request.UserId, ct);
        return cards.Select(SavedCardMapper.ToDto).ToList();
    }
}
