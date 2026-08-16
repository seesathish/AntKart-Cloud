using AK.ShoppingCart.Application.Interfaces;
using MediatR;

namespace AK.ShoppingCart.Application.Commands.ClearCart;

public sealed class ClearCartCommandHandler : IRequestHandler<ClearCartCommand, bool>
{
    private readonly IUnitOfWork _uow;

    public ClearCartCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<bool> Handle(ClearCartCommand request, CancellationToken ct)
    {
        var cart = await _uow.Carts.GetAsync(request.UserId, ct);
        if (cart is null) return false;

        cart.Clear();
        await _uow.Carts.SaveAsync(cart, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
