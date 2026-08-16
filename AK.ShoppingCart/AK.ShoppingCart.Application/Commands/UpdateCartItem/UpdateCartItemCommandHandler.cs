using AK.ShoppingCart.Application.Common;
using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Application.Interfaces;
using MediatR;

namespace AK.ShoppingCart.Application.Commands.UpdateCartItem;

public sealed class UpdateCartItemCommandHandler : IRequestHandler<UpdateCartItemCommand, CartDto>
{
    private readonly IUnitOfWork _uow;

    public UpdateCartItemCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<CartDto> Handle(UpdateCartItemCommand request, CancellationToken ct)
    {
        var cart = await _uow.Carts.GetAsync(request.UserId, ct)
            ?? throw new KeyNotFoundException($"Cart for user '{request.UserId}' not found");

        cart.UpdateItemQuantity(request.ProductId, request.Quantity);
        await _uow.Carts.SaveAsync(cart, ct);
        await _uow.SaveChangesAsync(ct);
        return CartMapper.ToDto(cart);
    }
}
