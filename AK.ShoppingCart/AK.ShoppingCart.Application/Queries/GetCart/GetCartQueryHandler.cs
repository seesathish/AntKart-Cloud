using AK.ShoppingCart.Application.Common;
using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Application.Interfaces;
using MediatR;

namespace AK.ShoppingCart.Application.Queries.GetCart;

public sealed class GetCartQueryHandler : IRequestHandler<GetCartQuery, CartDto?>
{
    private readonly IUnitOfWork _uow;

    public GetCartQueryHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<CartDto?> Handle(GetCartQuery request, CancellationToken ct)
    {
        var cart = await _uow.Carts.GetAsync(request.UserId, ct);
        return cart is null ? null : CartMapper.ToDto(cart);
    }
}
