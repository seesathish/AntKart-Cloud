using AK.ShoppingCart.Application.DTOs;
using MediatR;

namespace AK.ShoppingCart.Application.Commands.AddToCart;

public sealed record AddToCartCommand(string UserId, AddCartItemDto Item) : IRequest<CartDto>;
