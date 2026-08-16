using AK.Payments.Application.Common.Interfaces;
using MediatR;

namespace AK.Payments.Application.Commands.DeleteSavedCard;

public sealed class DeleteSavedCardCommandHandler(IUnitOfWork uow, IRazorpayClient razorpay)
    : IRequestHandler<DeleteSavedCardCommand>
{
    public async Task Handle(DeleteSavedCardCommand request, CancellationToken ct)
    {
        var card = await uow.SavedCards.GetByIdAsync(request.CardId, ct)
            ?? throw new KeyNotFoundException($"Card {request.CardId} not found.");

        if (card.UserId != request.UserId)
            throw new InvalidOperationException("Card does not belong to this user.");

        await razorpay.DeleteTokenAsync(card.RazorpayCustomerId, card.RazorpayTokenId, ct);
        await uow.SavedCards.DeleteAsync(request.CardId, ct);
        await uow.SaveChangesAsync(ct);
    }
}
