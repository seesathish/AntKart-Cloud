using AK.ShoppingCart.Application.Common;
using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Application.Interfaces;
using MediatR;

namespace AK.ShoppingCart.Application.Commands.RemoveFromCart;

public sealed class RemoveFromCartCommandHandler : IRequestHandler<RemoveFromCartCommand, CartDto>
{
    private readonly IUnitOfWork _uow;

    public RemoveFromCartCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<CartDto> Handle(RemoveFromCartCommand request, CancellationToken ct)
    {
        var cart = await _uow.Carts.GetAsync(request.UserId, ct)
            ?? throw new KeyNotFoundException($"Cart for user '{request.UserId}' not found");

        cart.RemoveItem(request.ProductId);
        await _uow.Carts.SaveAsync(cart, ct);
        await _uow.SaveChangesAsync(ct);
        return CartMapper.ToDto(cart);
    }
}
