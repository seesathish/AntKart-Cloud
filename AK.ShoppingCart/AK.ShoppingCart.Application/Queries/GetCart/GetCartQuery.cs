using AK.ShoppingCart.Application.DTOs;
using MediatR;

namespace AK.ShoppingCart.Application.Queries.GetCart;

public sealed record GetCartQuery(string UserId) : IRequest<CartDto?>;
