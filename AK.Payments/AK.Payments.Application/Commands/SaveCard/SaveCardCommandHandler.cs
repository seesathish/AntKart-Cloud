using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Application.Mapping;
using AK.Payments.Domain.Entities;
using MediatR;

namespace AK.Payments.Application.Commands.SaveCard;

public sealed class SaveCardCommandHandler(IUnitOfWork uow, IRazorpayClient razorpay)
    : IRequestHandler<SaveCardCommand, SavedCardDto>
{
    public async Task<SavedCardDto> Handle(SaveCardCommand request, CancellationToken ct)
    {
        var token = await razorpay.CreateTokenAsync(request.RazorpayCustomerId, request.RazorpayPaymentId, ct);

        var card = SavedCard.Create(
            request.UserId,
            request.RazorpayCustomerId,
            token.Id,
            token.CardNetwork,
            token.Last4,
            token.CardType,
            token.CardName);

        await uow.SavedCards.AddAsync(card, ct);
        await uow.SaveChangesAsync(ct);

        return SavedCardMapper.ToDto(card);
    }
}
