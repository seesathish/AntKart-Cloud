using AK.ShoppingCart.Application.DTOs;
using MediatR;

namespace AK.ShoppingCart.Application.Commands.RemoveFromCart;

public sealed record RemoveFromCartCommand(string UserId, string ProductId) : IRequest<CartDto>;
