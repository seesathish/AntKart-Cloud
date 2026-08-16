using AK.ShoppingCart.Application.Common;
using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Domain.Entities;
using MediatR;

namespace AK.ShoppingCart.Application.Commands.AddToCart;

public sealed class AddToCartCommandHandler : IRequestHandler<AddToCartCommand, CartDto>
{
    private readonly IUnitOfWork _uow;

    public AddToCartCommandHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<CartDto> Handle(AddToCartCommand request, CancellationToken ct)
    {
        var cart = await _uow.Carts.GetAsync(request.UserId, ct) ?? Cart.Create(request.UserId);
        var item = request.Item;
        cart.AddItem(item.ProductId, item.ProductName, item.SKU, item.Price, item.Quantity, item.ImageUrl);
        await _uow.Carts.SaveAsync(cart, ct);
        await _uow.SaveChangesAsync(ct);
        return CartMapper.ToDto(cart);
    }
}
