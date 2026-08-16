using AK.ShoppingCart.Application.DTOs;
using MediatR;

namespace AK.ShoppingCart.Application.Commands.UpdateCartItem;

public sealed record UpdateCartItemCommand(string UserId, string ProductId, int Quantity) : IRequest<CartDto>;
